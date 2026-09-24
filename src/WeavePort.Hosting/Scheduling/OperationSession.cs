using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed class OperationSession(IPluginSession session, TaskCompletionSource released, CancellationToken token) : IPluginSession
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
    private bool _disposed;
    public string Tenant => session.Tenant;
    public string Instance => session.Instance;

    public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        await _gate.WaitAsync(linked.Token);
        try
        {
            return await session.InvokeAsync(operation, payload, linked.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task RestartAsync(CancellationToken cancellationToken = default) => _disposed ? Task.CompletedTask : session.RestartAsync(cancellationToken);
    private readonly object _disposeSync = new();
    private Task? _disposal;
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            return new(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposed = true;
        try
        {
            await WeavePort.Internal.Cleanup.RunAsync(() => _stop.CancelAsync(), async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    await session.DisposeAsync();
                }
                finally
                {
                    _gate.Release();
                }
            });
        }
        finally
        {
            _stop.Dispose();
            released.TrySetResult();
        }
    }
}
