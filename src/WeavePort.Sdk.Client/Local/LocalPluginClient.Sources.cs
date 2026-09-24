using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Sdk.Client;
public sealed partial class LocalPluginClient
{
    /// <summary>Reads bounded binary blocks from a plugin source while retaining exclusive worker residency.</summary>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> SourceAsync(string operation, JsonElement input, int chunkBytes = ProtocolLimits.SourceChunkDefaultBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkBytes, ProtocolLimits.SourceChunkMinimumBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkBytes, ProtocolLimits.SourceChunkMaximumBytes);
        return ReadSourceAsync(operation, input, chunkBytes, cancellationToken);
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadSourceAsync(string operation, JsonElement input, int chunkBytes, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stop = CreateOperationSource(cancellationToken);
        _streamOptions.Validate();
        stop.CancelAfter(_streamOptions.TotalTimeout);
        await using IPluginSession target = await AcquireStreamAsync(stop.Token);
        string? source = null;
        bool healthy = false;
        bool done = false;
        try
        {
            CheckInput(input);
            JsonElement opened = await StreamExchangeAsync(target, SdkOperations.SourceOpen, new { operation, input }, stop.Token);
            source = opened.GetProperty(WireFields.Source).GetString() ?? throw new InvalidDataException("Missing source.");
            while (!done)
            {
                healthy = false;
                JsonElement reply = await StreamExchangeAsync(target, SdkOperations.SourceRead, new { source, chunkBytes }, stop.Token);
                if (!reply.GetProperty(WireFields.Data).TryGetBytesFromBase64(out byte[]? bytes))
                {
                    throw new InvalidDataException("Invalid source block encoding.");
                }

                done = reply.GetProperty(WireFields.Done).GetBoolean();
                if (bytes.Length > chunkBytes || !done && bytes.Length == 0)
                {
                    throw new InvalidDataException("Invalid source block.");
                }

                healthy = true;
                stop.Token.ThrowIfCancellationRequested();
                if (bytes.Length == 0)
                {
                    continue;
                }

                yield return bytes;
                stop.Token.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            if (!done && source is not null && !IsDisposed())
            {
                await CloseSourceAsync(target, source, healthy && !stop.IsCancellationRequested);
            }
        }
    }

    private async Task CloseSourceAsync(IPluginSession target, string source, bool healthy)
    {
        if (!healthy)
        {
            await target.RestartAsync();
            return;
        }

        using var cleanup = new CancellationTokenSource(_streamOptions.ExchangeTimeout);
        try
        {
            await StreamExchangeAsync(target, SdkOperations.SourceClose, new { source }, cleanup.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            await target.RestartAsync();
        }
    }

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

    private async Task<JsonElement> StreamExchangeAsync(IPluginSession target, string operation, object input, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_streamOptions.ExchangeTimeout);
        return await ExchangeAsync(target, operation, input, deadline.Token);
    }

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
