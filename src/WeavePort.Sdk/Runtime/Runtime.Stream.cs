using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;

internal sealed partial class Runtime
{
    private static readonly TimeSpan BatchFlushInterval = TimeSpan.FromMilliseconds(100);
    private async Task<object> NextAsync()
    {
        var items = new List<JsonElement>();
        long batchBytes = 2;
        bool done = false;
        while (items.Count < ProtocolLimits.StreamBatchItems)
        {
            var next = await ReadStreamItemAsync(items.Count != 0);
            done = next.Done;
            if (next.Item is not { } item)
            {
                break;
            }

            if (!TryAppendStreamItem(item, items, ref batchBytes))
            {
                break;
            }
        }

        if (done)
        {
            await CloseAsync();
        }

        return new
        {
            items,
            done
        };
    }

    private bool TryAppendStreamItem(JsonElement item, List<JsonElement> items, ref long batchBytes)
    {
        long size = JsonSize.Measure(item);
        if (size > ProtocolLimits.StreamItemBytes)
        {
            throw new InvalidDataException("Item limit.");
        }

        if (batchBytes + size + 1 > ProtocolLimits.StreamBatchBytes)
        {
            _pending = item;
            return false;
        }

        batchBytes += size + 1;
        _bytes += size;
        if (_bytes > ProtocolLimits.StreamTotalBytes)
        {
            throw new InvalidDataException("Stream limit.");
        }

        items.Add(item);
        return true;
    }

    private async Task<bool> AdvanceReadyAsync(bool hasItems)
    {
        if (hasItems)
        {
            // A prefetched callback must not hold already available items behind its reply.
            _sessionScope!.PauseCallbacks();
        }

        _advancement ??= _enumerator!.MoveNextAsync().AsTask();
        if (_advancement.IsCompleted)
        {
            return true;
        }

        if (hasItems)
        {
            return false;
        }

        try
        {
            await _advancement.WaitAsync(BatchFlushInterval);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async ValueTask<(JsonElement? Item, bool Done)> ReadStreamItemAsync(bool hasItems)
    {
        if (_pending is { } pending)
        {
            _pending = null;
            return (pending, false);
        }

        if (!await AdvanceReadyAsync(hasItems))
        {
            return (null, false);
        }

        bool available = await _advancement!;
        _advancement = null;
        if (!available)
        {
            return (null, true);
        }

        return (_enumerator!.Current, false);
    }

    private async Task<object> DispatchStreamAsync(string operation, JsonElement payload)
    {
        if (_streamId is null || payload.GetProperty(WireFields.Stream).GetString() != _streamId)
        {
            throw new InvalidDataException("Stream ownership.");
        }

        if (operation == SdkOperations.Close)
        {
            await CloseAsync();
            return new
            {
            };
        }

        return await NextAsync();
    }
}
