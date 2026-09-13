using WeavePort.Hosting;
using System.Text.Json;
using AppointmentDesk.Contracts;

namespace AppointmentDesk.Host;
internal sealed class CalendarStore : IAsyncDisposable
{
    private readonly string _path;
    private readonly FileStream _owner;
    private readonly SemaphoreSlim _gate = new(1);
    private Snapshot _state;
    internal CalendarStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "calendar.json");
        _owner = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > 2 * 1024 * 1024)
            {
                throw new InvalidDataException("Store exceeds 2 MiB.");
            }

            _state = File.Exists(_path) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path), Model.Json) ?? throw new InvalidDataException("Missing snapshot.") : new Snapshot(2, []);
            ValidateSnapshot(_state);
        }
        catch
        {
            _owner.Dispose();
            throw;
        }
    }

    internal async Task<Entry?> FindAsync(Request request, CancellationToken token)
    {
        Model.Validate(request);
        await _gate.WaitAsync(token);
        try
        {
            return Find(request);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Entry? Find(Request request)
    {
        var entry = _state.Entries.SingleOrDefault(e => Model.SameKey(e.Request, request));
        if (entry is not null && entry.Request != request)
        {
            throw new InvalidDataException("Request identity reused with changed input.");
        }

        return entry;
    }

    internal async Task<Slot[]> AvailableAsync(Request request, CancellationToken token)
    {
        Model.Validate(request);
        await _gate.WaitAsync(token);
        try
        {
            return Model.Slots.Where(s => Model.Fits(request.Wish, s) && !_state.Entries.Any(e => e.Request.Scope == request.Scope && e.Outcome?.Status == "booked" && e.Command.Slot == s)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<Entry> PrepareAsync(Request request, Slot slot, CancellationToken token, InstallationIdentity installation)
    {
        Model.Validate(request);
        if (!Model.Fits(request.Wish, slot))
        {
            throw new InvalidDataException("Invalid proposal.");
        }

        await _gate.WaitAsync(token);
        try
        {
            var existing = Find(request);
            if (existing is not null)
            {
                return existing;
            }

            if (_state.Entries.Length >= 1000)
            {
                throw new InvalidDataException("Store capacity reached (1000 intents).");
            }

            var entry = new Entry(request, new Command(request.Id, slot, request.Wish.Subject), null, installation);
            await SaveAsync(new Snapshot(2, [.._state.Entries, entry]), token);
            return entry;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<Outcome> BookAsync(Request request, Command command, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var entry = Find(request) ?? throw new UnauthorizedAccessException("No approved intent.");
            if (entry.Command != command)
            {
                throw new UnauthorizedAccessException("Command differs from approved intent.");
            }

            if (entry.Outcome is not null)
            {
                return entry.Outcome;
            }

            bool conflict = _state.Entries.Any(e => e.Request.Scope == request.Scope && e.Outcome?.Status == "booked" && e.Command.Slot == command.Slot);
            var outcome = new Outcome(conflict ? "conflict" : "booked", conflict ? null : Guid.NewGuid().ToString("N"), command.Slot);
            var updated = _state.Entries.Select(e => e == entry ? e with { Outcome = outcome } : e).ToArray();
            await SaveAsync(new Snapshot(2, updated), token);
            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(Snapshot state, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, Model.Json);
        if (bytes.Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("Store exceeds 2 MiB.");
        }

        string pending = _path + ".pending";
        try
        {
            await File.WriteAllBytesAsync(pending, bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(pending, _path, overwrite: true);
            _state = state;
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private static void ValidateSnapshot(Snapshot state)
    {
        if (state.Schema != 2 || state.Entries is null || state.Entries.Length > 1000)
        {
            throw new InvalidDataException("Invalid snapshot.");
        }

        var keys = new HashSet<(Scope, string)>();
        var booked = new HashSet<(Scope, Slot)>();
        var ids = new HashSet<string>();
        foreach (var entry in state.Entries)
        {
            if (entry?.Request is null || entry.Command is null || entry.Installation is null || entry.Installation.Plugin != "appointment-desk" || entry.Installation.Contract != "appointment-desk/v1" || !Model.Text(entry.Installation.Version) || entry.Installation.Digest?.Length != 64)
            {
                throw new InvalidDataException("Invalid intent.");
            }

            Model.Validate(entry.Request);
            if (!keys.Add((entry.Request.Scope, entry.Request.Id)) || entry.Command.RequestId != entry.Request.Id || entry.Command.Subject != entry.Request.Wish.Subject || !Model.Fits(entry.Request.Wish, entry.Command.Slot))
            {
                throw new InvalidDataException("Invalid intent identity or slot.");
            }

            if (entry.Outcome is not { } outcome)
            {
                continue;
            }

            if (outcome.Slot != entry.Command.Slot || outcome.Status is not ("booked" or "conflict") || (outcome.Status == "conflict" && outcome.BookingId is not null) || (outcome.Status == "booked" && (!Model.Text(outcome.BookingId) || !ids.Add(outcome.BookingId!) || !booked.Add((entry.Request.Scope, entry.Command.Slot)))))
            {
                throw new InvalidDataException("Invalid booking outcome.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _owner.DisposeAsync();
        _gate.Dispose();
    }
}
