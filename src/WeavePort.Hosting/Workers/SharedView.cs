using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;

internal sealed class SharedView(SharedPlugin plugin, string tenant) : IPluginOperationSession
{
    private bool _disposed;
    public string Tenant => tenant;
    public string Instance => "";
    public bool SupportsStreaming => false;

    public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (operation != SdkOperations.Call)
        {
            throw new NotSupportedException("Shared workers support unary SDK calls only.");
        }

        if (InvocationScope.Current.Value is not null)
        {
            return Task.FromResult(new InvocationResult(FailureCodes.Denied, JsonSerializer.SerializeToElement(new
            {
            }), "", 0));
        }

        return plugin.Host.InvokeSharedAsync(new SharedInvocationSession(plugin, tenant), operation, payload, cancellationToken);
    }

    public ValueTask<IPluginSession> AcquireOperationAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Shared workers do not support streams.");
    public Task RestartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Tenant views cannot restart shared workers.");
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
