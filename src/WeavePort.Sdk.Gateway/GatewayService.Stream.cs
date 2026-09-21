using System.Text.Json;
using Grpc.Core;
using WeavePort.Internal;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
public sealed partial class GatewayService
{
    private static async Task WriteLiveBatchesAsync(IAsyncEnumerable<JsonElement> source, IServerStreamWriter<Reply> output, GatewayRegistry.ActiveStream active)
    {
        CancellationToken token = active.Stop.Token;
        await using var iterator = source.GetAsyncEnumerator(token);
        Task<bool>? advance = null;
        long total = 0;
        var items = new List<JsonElement>();
        long bytes = 2;
        try
        {
            while (true)
            {
                advance ??= iterator.MoveNextAsync().AsTask();
                if (!advance.IsCompleted)
                {
                    await output.WriteAsync(Encode(items), token);
                    items.Clear();
                    bytes = 2;
                    if (!await ReadyAsync(advance, token))
                    {
                        continue;
                    }
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
                if (size > 128 << 10 || total > 64 << 20)
                {
                    throw new PluginCallException("stream-limit");
                }

                if (items.Count == 16 || bytes + size + 1 > 256 << 10)
                {
                    await output.WriteAsync(Encode(items), token);
                    items.Clear();
                    bytes = 2;
                }

                items.Add(item);
                bytes += size + 1;
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
            await advance.WaitAsync(TimeSpan.FromMilliseconds(100), token);
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
}
