using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed partial class PluginSession
{
    private readonly SemaphoreSlim _operationGate = new(1);
    public bool SupportsStreaming => true;

    public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            return new("busy", JsonSerializer.SerializeToElement(new { }), Instance, 0);
        }

        try
        {
            return await InvokeCoreAsync(operation, payload, false, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask<IPluginSession> AcquireOperationAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken);
        if (_disposed)
        {
            _operationGate.Release();
            throw new ObjectDisposedException(nameof(PluginSession));
        }

        return new DirectOperation(this, cancellationToken);
    }

    private sealed class DirectOperation(PluginSession owner, CancellationToken token) : IPluginSession
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        private Task? _disposal;
        private bool _started;
        private bool _complete;
        public string Tenant => owner.Tenant;
        public string Instance => owner.Instance;

        public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposal is not null, this);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            _started = true;
            var result = await owner.InvokeCoreAsync(operation, payload, true, linked.Token);
            if (result.Status == "ok")
            {
                _complete = operation is "$sdk.close" or "$sdk.source.close" || result.Value.ValueKind == JsonValueKind.Object && result.Value.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True;
            }

            return result;
        }

        public Task RestartAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return _disposal is not null ? Task.CompletedTask : owner.RestartAsync(cancellationToken);
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                return new(_disposal ??= Task.Run(DisposeCoreAsync));
            }
        }

        private async Task DisposeCoreAsync()
        {
            try
            {
                await WeavePort.Internal.Cleanup.RunAsync(() => _stop.CancelAsync(), async () =>
                {
                    if (_started && !_complete)
                    {
                        await owner.RestartAsync();
                    }
                });
            }
            finally
            {
                _stop.Dispose();
                owner._operationGate.Release();
            }
        }
    }
}
