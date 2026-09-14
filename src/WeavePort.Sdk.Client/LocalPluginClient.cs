using WeavePort.Internal;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Sdk.Client;
/// <summary>Owns an existing host binding and exposes author-SDK operations over it.</summary>
public sealed class LocalPluginClient(IPluginSession session, TimeSpan? streamTimeout = null) : IPluginClient
{
    private readonly object _disposeSync = new();
    private Task? _disposal;
    private bool _disposed;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeSpan _streamTimeout = streamTimeout ?? TimeSpan.FromSeconds(30);
    /// <summary>The immutable host-bound customer, never taken from an operation input.</summary>
    public string Tenant => session.Tenant;
    /// <summary>Current worker instance for host diagnostics.</summary>
    public string Instance => session.Instance;

    /// <inheritdoc/>
    public async Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
    {
        using var stop = CreateOperationSource(cancellationToken);
        if (!await _gate.WaitAsync(0, stop.Token))
        {
            throw new PluginCallException("busy", false);
        }

        try
        {
            CheckInput(input);
            JsonElement output = await ExchangeAsync("$sdk.call", new { operation, input }, stop.Token);
            if (Size(output) > 512 << 10)
            {
                throw new PluginCallException("value-limit");
            }

            return output;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var stop = CreateOperationSource(cancellationToken);
        stop.CancelAfter(_streamTimeout);
        if (!await _gate.WaitAsync(0, stop.Token))
        {
            throw new PluginCallException("busy", false);
        }

        string? stream = null;
        bool done = false;
        bool healthy = true;
        long total = 0;
        try
        {
            CheckInput(input);
            JsonElement opened = await ExchangeAsync("$sdk.start", new { operation, input }, stop.Token);
            stream = opened.GetProperty("stream").GetString() ?? throw new InvalidDataException("Missing stream.");
            while (!done)
            {
                healthy = false;
                JsonElement batch = await ExchangeAsync("$sdk.next", new { stream }, stop.Token);
                healthy = true;
                JsonElement items = batch.GetProperty("items");
                done = batch.GetProperty("done").GetBoolean();
                ValidateBatch(items, done);
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
            try
            {
                if (!done && stream is not null && !IsDisposed())
                {
                    await CloseStreamAsync(stream, healthy && !stop.IsCancellationRequested);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static void ValidateBatch(JsonElement items, bool done)
    {
        if (items.GetArrayLength() > 16 || Size(items) > 256 << 10)
        {
            throw new InvalidDataException("Invalid batch.");
        }

        if (!done && items.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Empty unfinished batch.");
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

    private async Task CloseStreamAsync(string stream, bool healthy)
    {
        if (!healthy)
        {
            await session.RestartAsync();
            return;
        }

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await ExchangeAsync("$sdk.close", new { stream }, cleanup.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            await session.RestartAsync();
        }
    }

    private async Task<JsonElement> ExchangeAsync(string operation, object input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        InvocationResult reply = await session.InvokeAsync(operation, JsonSerializer.SerializeToElement(input), token);
        if (reply.Status == "cancelled")
        {
            throw new OperationCanceledException(token);
        }

        if (reply.Status != "ok")
        {
            throw new PluginCallException(reply.Status, reply.MayHaveExecuted);
        }

        return reply.Value;
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
