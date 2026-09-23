using WeavePort.Internal;
using System.Runtime.CompilerServices;
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
        InvocationResult reply = await ExchangeResultAsync(session, SdkOperations.Call, new { operation, input }, stop.Token);
        JsonElement output = reply.Value;
        if (Size(output) > 512 << 10)
        {
            throw new PluginCallException("value-limit");
        }

        return new(output, reply.ElapsedMs);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var stop = CreateOperationSource(cancellationToken);
        _streamOptions.Validate();
        stop.CancelAfter(_streamOptions.TotalTimeout);
        await using IPluginSession operationSession = await AcquireStreamAsync(stop.Token);
        string? stream = null;
        bool done = false;
        bool healthy = true;
        long total = 0;
        try
        {
            CheckInput(input);
            JsonElement opened = await StreamExchangeAsync(operationSession, SdkOperations.Start, new { operation, input }, stop.Token);
            stream = opened.GetProperty("stream").GetString() ?? throw new InvalidDataException("Missing stream.");
            while (!done)
            {
                healthy = false;
                JsonElement batch = await StreamExchangeAsync(operationSession, SdkOperations.Next, new { stream }, stop.Token);
                healthy = true;
                JsonElement items = batch.GetProperty("items");
                done = batch.GetProperty("done").GetBoolean();
                ValidateBatch(items);
                foreach (JsonElement item in items.EnumerateArray())
                {
                    stop.Token.ThrowIfCancellationRequested();
                    ValidateItem(item, ref total);
                    yield return item;
                }

                stop.Token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            if (!done && stream is not null && !IsDisposed())
            {
                await CloseStreamAsync(operationSession, stream, healthy && !stop.IsCancellationRequested);
            }
        }
    }

    private static void ValidateBatch(JsonElement items)
    {
        if (items.GetArrayLength() > 16 || Size(items) > 256 << 10)
        {
            throw new InvalidDataException("Invalid batch.");
        }
    }

    private static void ValidateItem(JsonElement item, ref long total)
    {
        long bytes = Size(item);
        total += bytes;
        if (bytes > 128 << 10 || total > 64 << 20)
        {
            throw new InvalidDataException("Stream limit.");
        }
    }

    private async Task CloseStreamAsync(IPluginSession operationSession, string stream, bool healthy)
    {
        if (!healthy)
        {
            await operationSession.RestartAsync();
            return;
        }

        using var cleanup = new CancellationTokenSource(_streamOptions.ExchangeTimeout);
        try
        {
            await StreamExchangeAsync(operationSession, SdkOperations.Close, new { stream }, cleanup.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            await operationSession.RestartAsync();
        }
    }

    private static async Task<JsonElement> ExchangeAsync(IPluginSession target, string operation, object input, CancellationToken token) => (await ExchangeResultAsync(target, operation, input, token)).Value;
    private static async Task<InvocationResult> ExchangeResultAsync(IPluginSession target, string operation, object input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        InvocationResult reply = await target.InvokeAsync(operation, JsonSerializer.SerializeToElement(input), token);
        if (reply.Status == "cancelled")
        {
            throw new OperationCanceledException(token);
        }

        if (reply.Status != "ok")
        {
            throw new PluginCallException(reply.Status, reply.MayHaveExecuted)
            {
                VersionMismatch = reply.VersionMismatch
            };
        }

        return reply;
    }

    private static long Size(JsonElement value) => JsonSize.Measure(value);
    private static void CheckInput(JsonElement input)
    {
        if (Size(input) > 512 << 10)
        {
            throw new PluginCallException("input-limit", false);
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
