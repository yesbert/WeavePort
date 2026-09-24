using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Sdk.Client;
/// <summary>Owns an existing host binding and exposes author-SDK operations over it.</summary>
public sealed partial class LocalPluginClient(IPluginSession session, TimeSpan? streamTimeout = null, PluginStreamOptions? streamOptions = null) : IBoundPluginClient
{
    private readonly object _disposeSync = new();
    private Task? _disposal;
    private bool _disposed;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PluginStreamOptions _streamOptions = streamOptions ?? new PluginStreamOptions
    {
        TotalTimeout = streamTimeout ?? TimeSpan.FromMinutes(5)
    };
    /// <summary>The immutable host-bound customer, never taken from an operation input.</summary>
    public string Tenant => session.Tenant;

    /// <inheritdoc/>
    public Task<string> GetTenantAsync(CancellationToken cancellationToken = default)
    {
        using var stop = CreateOperationSource(cancellationToken);
        stop.Token.ThrowIfCancellationRequested();
        return Task.FromResult(Tenant);
    }

    /// <summary>Current worker instance for host diagnostics.</summary>
    public string Instance => session.Instance;

    /// <inheritdoc/>
    public async Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => (await CallWithMetadataAsync(operation, input, cancellationToken)).Value;
    /// <inheritdoc/>
    public async Task<PluginCallResult<JsonElement>> CallWithMetadataAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
    {
        using var stop = CreateOperationSource(cancellationToken);
        await using IPluginSession? legacyLease = session is IPluginOperationSession ? null : await AcquireStreamAsync(stop.Token);
        CheckInput(input);
        InvocationResult reply = await ExchangeResultAsync(session, SdkOperations.Call, new
        {
            operation,
            input
        }, stop.Token);
        JsonElement output = reply.Value;
        if (Size(output) > ProtocolLimits.UnaryBytes)
        {
            throw new PluginCallException(FailureCodes.ValueLimit);
        }

        return new(output, reply.ElapsedMs);
    }

    private async Task<JsonElement> StreamExchangeAsync(IPluginSession target, string operation, object input, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_streamOptions.ExchangeTimeout);
        return await ExchangeAsync(target, operation, input, deadline.Token);
    }

    private static async Task<JsonElement> ExchangeAsync(IPluginSession target, string operation, object input, CancellationToken token) => (await ExchangeResultAsync(target, operation, input, token)).Value;
    private static async Task<InvocationResult> ExchangeResultAsync(IPluginSession target, string operation, object input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        InvocationResult reply = await target.InvokeAsync(operation, JsonSerializer.SerializeToElement(input), token);
        if (reply.Status == FailureCodes.Cancelled)
        {
            throw new OperationCanceledException(token);
        }

        if (reply.Status != FailureCodes.Ok)
        {
            throw new PluginCallException(reply.Status, reply.MayHaveExecuted)
            {
                Failure = reply.Failure,
                VersionMismatch = reply.VersionMismatch
            };
        }

        return reply;
    }

    private static long Size(JsonElement value) => JsonSize.Measure(value);
    private static void CheckInput(JsonElement input)
    {
        if (Size(input) > ProtocolLimits.UnaryBytes)
        {
            throw new PluginCallException(FailureCodes.InputLimit, false);
        }
    }

    /// <summary>Cancels outstanding work and disposes the owned host binding.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposed = true;
            return new ValueTask(_disposal ??= DisposeCoreAsync());
        }
    }

    private CancellationTokenSource CreateOperationSource(CancellationToken token)
    {
        lock (_disposeSync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        }
    }

    private bool IsDisposed()
    {
        lock (_disposeSync)
        {
            return _disposed;
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await Cleanup.RunAsync(() => _lifetime.CancelAsync(), () => session.DisposeAsync().AsTask());
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
