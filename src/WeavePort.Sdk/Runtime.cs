using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Sdk;
internal sealed class Runtime(PluginApplication application)
{
    private Channel _channel = null!;
    private JsonElement _request;
    private IAsyncEnumerator<JsonElement>? _enumerator;
    private CancellationTokenSource? _streamCancellation;
    private JsonElement? _pending;
    private string? _streamId;
    private long _bytes;
    private int _callback;
    private readonly SemaphoreSlim _callbackGate = new(1);
    private readonly AsyncLocal<CallScope?> _scope = new();
    private sealed class CallScope(JsonElement request)
    {
        internal JsonElement Request { get; } = request;

        internal bool Active = true;
    }

    internal async Task RunAsync(CancellationToken token)
    {
        await using var channel = new Channel();
        _channel = channel;
        await channel.WriteAsync(new { type = "ready", protocol = 1, pluginVersion = application.PluginVersion }, token);
        try
        {
            while (await channel.ReadAsync(token)is { } request)
            {
                _request = request;
                var scope = new CallScope(request);
                _scope.Value = scope;
                try
                {
                    object result = await DispatchAsync(request.GetProperty("operation").GetString()!, request.GetProperty("payload"), token);
                    await channel.WriteAsync(new { type = "result", id = request.GetProperty("id").GetString(), value = result }, token);
                }
                catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
                {
                    await CloseAsync();
                    await channel.WriteAsync(new { type = "error", id = request.GetProperty("id").GetString(), code = "sdk-error" }, token);
                }
                finally
                {
                    scope.Active = false;
                    _scope.Value = null;
                }
            }
        }
        finally
        {
            await CloseAsync();
        }
    }

    private async Task<object> DispatchAsync(string operation, JsonElement payload, CancellationToken token)
    {
        if (operation is "$sdk.next" or "$sdk.close")
        {
            if (_streamId is null || payload.GetProperty("stream").GetString() != _streamId)
            {
                throw new InvalidDataException("Stream ownership.");
            }

            if (operation == "$sdk.close")
            {
                await CloseAsync();
                return new
                {
                };
            }

            return await NextAsync();
        }

        if (_enumerator is not null)
        {
            throw new InvalidOperationException("Stream already active.");
        }

        string name = payload.GetProperty("operation").GetString()!;
        JsonElement input = payload.GetProperty("input");
        var context = new PluginCallContext(_request.GetProperty("context"), CallbackAsync);
        if (operation == "$sdk.call")
        {
            return await application.Functions[name](input, context, token);
        }

        if (operation != "$sdk.start")
        {
            throw new InvalidDataException("Unknown SDK operation.");
        }

        _streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _enumerator = application.Streams[name](input, context, _streamCancellation.Token).GetAsyncEnumerator(_streamCancellation.Token);
        _streamId = Guid.NewGuid().ToString("N");
        _bytes = 0;
        return new
        {
            stream = _streamId
        };
    }

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
                if (!await _enumerator!.MoveNextAsync())
                {
                    done = true;
                    break;
                }

                item = _enumerator.Current;
            }

            long size = JsonSize.Measure(item);
            if (size > 128 << 10)
            {
                throw new InvalidDataException("Item limit.");
            }

            if (batchBytes + size + 1 > 256 << 10)
            {
                _pending = item;
                break;
            }

            batchBytes += size + 1;
            _bytes += size;
            if (_bytes > 64 << 20)
            {
                throw new InvalidDataException("Stream limit.");
            }

            items.Add(item);
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

    private async Task<JsonElement> CallbackAsync(string operation, JsonElement input, CancellationToken token)
    {
        CallScope scope = _scope.Value ?? throw new InvalidOperationException("No active invocation.");
        await _callbackGate.WaitAsync(token);
        try
        {
            if (!scope.Active)
            {
                throw new InvalidOperationException("Expired invocation.");
            }

            string id = scope.Request.GetProperty("id").GetString()!;
            string callbackId = (++_callback).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _channel.WriteAsync(new { type = "callback", id, callbackId, operation, payload = input }, token);
            JsonElement reply = await _channel.ReadAsync(token) ?? throw new EndOfStreamException();
            if (reply.GetProperty("type").GetString() != "callback-result" || reply.GetProperty("id").GetString() != id || reply.GetProperty("callbackId").GetString() != callbackId)
            {
                throw new InvalidDataException("Callback identity.");
            }

            return reply.GetProperty("value");
        }
        finally
        {
            _callbackGate.Release();
        }
    }

    private async Task CloseAsync()
    {
        var enumerator = _enumerator;
        _enumerator = null;
        _streamId = null;
        _pending = null;
        try
        {
            if (_streamCancellation is not null)
            {
                await _streamCancellation.CancelAsync();
            }

            if (enumerator is not null)
            {
                await enumerator.DisposeAsync();
            }
        }
        finally
        {
            _streamCancellation?.Dispose();
            _streamCancellation = null;
        }
    }
}
