using WeavePort.Hosting;
using System.Text.Json;
using AppointmentDesk.Contracts;

namespace AppointmentDesk.Host;
internal static class StorageVerification
{
    private static readonly InstallationIdentity Pin = new("appointment-desk", "1", "appointment-desk/v1", new string ('A', 64));
    internal static async Task RunAsync(string root, CancellationToken token)
    {
        string owned = Path.Combine(root, "owned");
        await using (var first = new CalendarStore(owned))
        {
            await Verification.RefusedAsync(async () =>
            {
                await using var second = new CalendarStore(owned);
            });
            Verification.Check(true, "second coordinator cannot own the same calendar store");
        }

        await using (var reopened = new CalendarStore(owned))
        {
            Verification.Check(true, "released store can be reopened");
        }

        await WriteFailureAsync(root, token);
        string bad = Path.Combine(root, "bad");
        Directory.CreateDirectory(bad);
        string badPath = Path.Combine(bad, "calendar.json");
        await File.WriteAllTextAsync(badPath, "{broken", token);
        await Verification.RefusedAsync(async () =>
        {
            await using var store = new CalendarStore(bad);
        });
        Verification.Check(await File.ReadAllTextAsync(badPath, token) == "{broken", "malformed store is refused without resetting history");
        var request = Verification.Request("original");
        var entry = new Entry(request, new Command(request.Id, Model.Slots[0], request.Wish.Subject), new Outcome("booked", "original-booking", Model.Slots[0]), Pin);
        await File.WriteAllTextAsync(badPath, JsonSerializer.Serialize(new Snapshot(2, [entry, entry]), Model.Json), token);
        await Verification.RefusedAsync(async () =>
        {
            await using var store = new CalendarStore(bad);
        });
        Verification.Check(true, "duplicate persisted request identity is refused");
        string full = Path.Combine(root, "full");
        Directory.CreateDirectory(full);
        var entries = Enumerable.Range(1, 999).Select(i =>
        {
            var r = Verification.Request("pending-" + i);
            return new Entry(r, new Command(r.Id, Model.Slots[1], r.Wish.Subject), null, Pin);
        }).Prepend(entry).ToArray();
        await File.WriteAllTextAsync(Path.Combine(full, "calendar.json"), JsonSerializer.Serialize(new Snapshot(2, entries), Model.Json), token);
        await using (var store = new CalendarStore(full))
        {
            await Verification.RefusedAsync(() => store.PrepareAsync(Verification.Request("overflow"), Model.Slots[2], token, Pin));
            Verification.Check((await store.FindAsync(request, token))?.Outcome == entry.Outcome, "capacity refusal preserves existing booking");
            Verification.Check(await store.BookAsync(request, entry.Command, token) == entry.Outcome, "full store still reconciles an existing outcome");
        }

        await File.WriteAllBytesAsync(badPath, new byte[2 * 1024 * 1024 + 1], token);
        await Verification.RefusedAsync(async () =>
        {
            await using var store = new CalendarStore(bad);
        });
        Verification.Check(new FileInfo(badPath).Length == 2 * 1024 * 1024 + 1, "oversized store is refused without modification");
    }

    private static async Task WriteFailureAsync(string root, CancellationToken token)
    {
        string writeFailure = Path.Combine(root, "write-failure");
        await using (var store = new CalendarStore(writeFailure))
        {
            var r = Verification.Request("write-failure");
            var prepared = await store.PrepareAsync(r, Model.Slots[0], token, Pin);
            string pending = Path.Combine(writeFailure, "calendar.json.pending");
            Directory.CreateDirectory(pending);
            await Verification.RefusedAsync(() => store.BookAsync(r, prepared.Command, token));
            Verification.Check((await store.FindAsync(r, token))?.Outcome is null, "failed persistence does not publish an in-memory booking");
            Directory.Delete(pending);
            Verification.Check((await store.BookAsync(r, prepared.Command, token)).Status == "booked", "retry after storage failure commits retained intent");
        }
    }
}
