using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;
internal sealed partial class Runtime
{
    private async Task<object> NextAsync()
    {
        var items = new List<JsonElement>();
        long batchBytes = 2;
        bool done = false;
        while (items.Count < 16)
        {
            JsonElement item;
            if (_pending is { } pending)
            {
                item = pending;
                _pending = null;
            }
            else
            {
                if (!await AdvanceReadyAsync(items.Count != 0))
                {
                    break;
                }

                bool available = await _advancement!;
                _advancement = null;
                if (!available)
                {
                    done = true;
                    break;
                }

                item = _enumerator!.Current;
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
        if (size > 128 << 10)
        {
            throw new InvalidDataException("Item limit.");
        }

        if (batchBytes + size + 1 > 256 << 10)
        {
            _pending = item;
            return false;
        }

        batchBytes += size + 1;
        _bytes += size;
        if (_bytes > 64 << 20)
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
            await _advancement.WaitAsync(TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
