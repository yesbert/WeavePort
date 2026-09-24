using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Sdk.Client;

public sealed partial class LocalPluginClient
{
    private async ValueTask<IPluginSession> AcquireStreamAsync(CancellationToken token)
    {
        if (session is not IPluginOperationSession operations)
        {
            await _gate.WaitAsync(token);
            return new LegacyLease(session, _gate);
        }

        if (!operations.SupportsStreaming)
        {
            throw new NotSupportedException("Shared bindings do not support streams or binary sources.");
        }

        return await operations.AcquireOperationAsync(token);
    }

    // Legacy sessions lack operation reservations, so all calls share this client-local gate.
    private sealed class LegacyLease(IPluginSession target, SemaphoreSlim gate) : IPluginSession
    {
        private int _disposed;
        public string Tenant => target.Tenant;
        public string Instance => target.Instance;

        public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default) => target.InvokeAsync(operation, payload, cancellationToken);
        public Task RestartAsync(CancellationToken cancellationToken = default) => target.RestartAsync(cancellationToken);
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
