using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Sdk.Client;

public sealed partial class LocalPluginClient
{
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
            JsonElement opened = await StreamExchangeAsync(operationSession, SdkOperations.Start, new
            {
                operation,
                input
            }, stop.Token);
            stream = opened.GetProperty(WireFields.Stream).GetString() ?? throw new InvalidDataException("Missing stream.");
            var buffered = new Queue<JsonElement>();
            while (!done || buffered.Count != 0)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (buffered.Count == 0)
                {
                    healthy = false;
                    JsonElement batch = await StreamExchangeAsync(operationSession, SdkOperations.Next, new
                    {
                        stream
                    }, stop.Token);
                    healthy = true;
                    JsonElement items = batch.GetProperty(WireFields.Items);
                    done = batch.GetProperty(WireFields.Done).GetBoolean();
                    ValidateBatch(items);
                    buffered = new Queue<JsonElement>(items.EnumerateArray());
                    continue;
                }

                JsonElement item = buffered.Dequeue();
                ValidateItem(item, ref total);
                yield return item;
            }

            stop.Token.ThrowIfCancellationRequested();
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
        if (items.GetArrayLength() > ProtocolLimits.StreamBatchItems || Size(items) > ProtocolLimits.StreamBatchBytes)
        {
            throw new InvalidDataException("Invalid batch.");
        }
    }

    private static void ValidateItem(JsonElement item, ref long total)
    {
        long bytes = Size(item);
        total += bytes;
        if (bytes > ProtocolLimits.StreamItemBytes || total > ProtocolLimits.StreamTotalBytes)
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
            await StreamExchangeAsync(operationSession, SdkOperations.Close, new
            {
                stream
            }, cleanup.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            await operationSession.RestartAsync();
        }
    }
}
