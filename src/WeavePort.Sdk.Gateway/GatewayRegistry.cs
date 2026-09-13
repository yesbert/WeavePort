using System.Security.Cryptography;
using WeavePort.Internal;
using WeavePort.Sdk.Client;

namespace WeavePort.Sdk.Gateway;
/// <summary>Trusted application registration of binding-scoped gateway credentials; no remote executable registration.</summary>
public sealed class GatewayRegistry : IAsyncDisposable
{
    internal sealed class ActiveStream(string id, CancellationToken token) : IDisposable
    {
        internal string Id { get; } = id;
        internal CancellationTokenSource Stop { get; } = CancellationTokenSource.CreateLinkedTokenSource(token);
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal async Task CancelAsync()
        {
            try
            {
                await Stop.CancelAsync();
            }
            catch (ObjectDisposedException) when (Done.Task.IsCompleted)
            {
            // The request completed between lookup and cancellation.
            }
        }

        public void Dispose()
        {
            Done.TrySetResult();
            Stop.Dispose();
        }
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, IPluginClient> _bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveStream> _streams = new(StringComparer.Ordinal);
    private readonly List<Task> _revocations = [];
    private bool _disposed;
    private Task? _disposal;
    internal ActiveStream Begin(string token, string id, CancellationToken cancellation)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("Invalid stream identity.");
        }

        lock (_sync)
        {
            _ = Get(token);
            if (_streams.ContainsKey(token))
            {
                throw new PluginCallException("busy", false);
            }

            var stream = new ActiveStream(id, cancellation);
            _streams.Add(token, stream);
            return stream;
        }
    }

    internal async Task CancelAsync(string token, string id, CancellationToken cancellation)
    {
        ActiveStream? stream;
        lock (_sync)
        {
            _streams.TryGetValue(token, out stream);
        }

        if (stream is not null && stream.Id == id)
        {
            await stream.CancelAsync();
            await stream.Done.Task.WaitAsync(cancellation);
        }
    }

    internal void End(string token, ActiveStream stream)
    {
        lock (_sync)
        {
            if (_streams.GetValueOrDefault(token) == stream)
            {
                _streams.Remove(token);
            }
        }

        stream.Dispose();
    }

    /// <summary>Registers a preconfigured host client and returns an unguessable invocation credential.</summary>
    public string Register(IPluginClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _bindings.Add(token, client);
        }

        return token;
    }

    internal IPluginClient Get(string? token)
    {
        lock (_sync)
        {
            return !_disposed && token is not null && _bindings.TryGetValue(token, out var client) ? client : throw new UnauthorizedAccessException();
        }
    }

    /// <summary>Revokes access and disposes the binding, cancelling its outstanding work.</summary>
    public Task RevokeAsync(string token)
    {
        lock (_sync)
        {
            if (!_bindings.Remove(token, out var client))
            {
                return Task.CompletedTask;
            }

            _streams.Remove(token, out var stream);
            _revocations.RemoveAll(task => task.IsCompleted);
            Task cleanup = ReleaseAsync(client, stream);
            _revocations.Add(cleanup);
            return cleanup;
        }
    }

    private static Task ReleaseAsync(IPluginClient client, ActiveStream? stream)
    {
        return Cleanup.RunAsync(() => client.DisposeAsync().AsTask(), () => stream?.CancelAsync() ?? Task.CompletedTask, () => stream?.Done.Task ?? Task.CompletedTask);
    }

    /// <summary>Closes registration, revokes all credentials and attempts every binding cleanup.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposal is null)
            {
                _disposed = true;
                var pending = _bindings.Select(pair =>
                {
                    _streams.TryGetValue(pair.Key, out var stream);
                    return (Client: pair.Value, Stream: stream);
                }).ToArray();
                _bindings.Clear();
                _streams.Clear();
                Func<Task>[] actions = pending.Select(item => (Func<Task>)(() => ReleaseAsync(item.Client, item.Stream))).Concat(_revocations.Select(task => (Func<Task>)(() => task))).ToArray();
                _revocations.Clear();
                _disposal = Cleanup.RunAsync(actions);
            }

            return new ValueTask(_disposal);
        }
    }
}
