using System.Text.Json;
using Grpc.Core;
using WeavePort.Internal;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class GatewayService
{
    private const int JsonArrayBracketsBytes = 2;
    private const int JsonItemSeparatorBytes = 1;
    private static readonly TimeSpan BatchFlushInterval = TimeSpan.FromMilliseconds(100);
    private static async Task WriteLiveBatchesAsync(IAsyncEnumerable<JsonElement> source, IServerStreamWriter<Reply> output, GatewayRegistry.ActiveStream active)
    {
        CancellationToken token = active.Stop.Token;
        await using var iterator = source.GetAsyncEnumerator(token);
        Task<bool>? advance = null;
        long total = 0;
        var items = new List<JsonElement>();
        long bytes = JsonArrayBracketsBytes;
        try
        {
            while (true)
            {
                advance ??= iterator.MoveNextAsync().AsTask();
                var waiting = await FlushWhileWaitingAsync(advance, items, output, bytes, token);
                bytes = waiting.Bytes;
                if (!waiting.Ready)
                {
                    continue;
                }

                bool available = await advance;
                advance = null;
                if (!available)
                {
                    break;
                }

                JsonElement item = iterator.Current;
                long size = JsonSize.Measure(item);
                total += size;
                if (size > ProtocolLimits.StreamItemBytes || total > ProtocolLimits.StreamTotalBytes)
                {
                    throw new PluginCallException(FailureCodes.StreamLimit);
                }

                bytes = await FlushFullBatchAsync(items, output, bytes, size, token);
                items.Add(item);
                bytes += size + JsonItemSeparatorBytes;
            }

            if (items.Count != 0)
            {
                await output.WriteAsync(Encode(items), token);
            }
        }
        finally
        {
            await CompleteAdvanceAsync(advance, active);
        }
    }

    private static async Task<bool> ReadyAsync(Task<bool> advance, CancellationToken token)
    {
        try
        {
            await advance.WaitAsync(BatchFlushInterval, token);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task CompleteAdvanceAsync(Task<bool>? advance, GatewayRegistry.ActiveStream active)
    {
        if (advance is null)
        {
            return;
        }

        await active.Stop.CancelAsync();
        try
        {
            await advance;
        }
        catch (OperationCanceledException) when (active.Stop.IsCancellationRequested)
        {
            // The outstanding advancement has released its iterator before disposal.
        }
    }

    private static async Task<(bool Ready, long Bytes)> FlushWhileWaitingAsync(Task<bool> advance, List<JsonElement> items, IServerStreamWriter<Reply> output, long bytes, CancellationToken token)
    {
        if (advance.IsCompleted)
        {
            return (true, bytes);
        }

        await output.WriteAsync(Encode(items), token);
        items.Clear();
        return (await ReadyAsync(advance, token), JsonArrayBracketsBytes);
    }

    private static async Task<long> FlushFullBatchAsync(List<JsonElement> items, IServerStreamWriter<Reply> output, long bytes, long size, CancellationToken token)
    {
        if (items.Count < ProtocolLimits.StreamBatchItems && bytes + size + JsonItemSeparatorBytes <= ProtocolLimits.StreamBatchBytes)
        {
            return bytes;
        }

        await output.WriteAsync(Encode(items), token);
        items.Clear();
        return JsonArrayBracketsBytes;
    }
}
