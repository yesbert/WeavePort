using System.Collections.Concurrent;
using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;
internal sealed partial class ConcurrentRuntime(PluginApplication application)
{
    private readonly ConcurrentDictionary<string, Task> _running = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Invocation> _calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _completed = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _completedOrder = new();
    private Channel _channel = null!;
    private CancellationTokenSource _lifetime = null!;
    private int _degree;
    private sealed class Invocation(string id, CancellationToken lifetime) : IDisposable
    {
        internal string Id { get; } = id;
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        internal ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> Callbacks { get; } = new(StringComparer.Ordinal);
        internal Task Completion { get; set; } = Task.CompletedTask;

        internal int CallbackSequence;
        internal volatile bool Active = true;
        private readonly object _sync = new();
        private bool _finished;
        internal void Cancel()
        {
            lock (_sync)
            {
                if (!_finished)
                {
                    Cancellation.Cancel();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _finished = true;
                Cancellation.Dispose();
            }
        }
    }

    internal async Task RunAsync(CancellationToken token)
    {
        await using var channel = new Channel();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        _channel = channel;
        _lifetime = lifetime;
        await channel.WriteAsync(new { type = "ready", protocol = 2, concurrentCalls = 1, sessionCleanup = 1, pluginVersion = application.PluginVersion }, token);
        JsonElement configure = await channel.ReadAsync(token) ?? throw new EndOfStreamException();
        if (configure.GetProperty("type").GetString() != "configure" || !configure.GetProperty("degree").TryGetInt32(out _degree) || _degree is < 1 or > 1024)
        {
            throw new InvalidDataException("Invalid concurrency configuration.");
        }

        try
        {
            while (await channel.ReadAsync(lifetime.Token)is { } frame)
            {
                Route(frame);
            }
        }
        finally
        {
            await lifetime.CancelAsync();
            await Task.WhenAll(_running.Values);
        }
    }

    private void Route(JsonElement frame)
    {
        string id = frame.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing invocation identity.");
        string? type = frame.GetProperty("type").GetString();
        if (type == "invoke")
        {
            if (_completed.ContainsKey(id))
            {
                throw new InvalidDataException("Repeated invocation identity.");
            }

            if (_calls.Count >= _degree)
            {
                throw new InvalidDataException("Concurrency limit exceeded.");
            }

            var call = new Invocation(id, _lifetime.Token);
            if (!_calls.TryAdd(id, call))
            {
                call.Dispose();
                throw new InvalidDataException("Duplicate invocation identity.");
            }

            call.Completion = Task.Run(() => ExecuteAsync(call, frame));
            _running[id] = call.Completion;
            _ = call.Completion.ContinueWith(_ => _running.TryRemove(id, out Task? completed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        if (!_calls.TryGetValue(id, out Invocation? existing))
        {
            if (type == "cancel" && _completed.ContainsKey(id))
            {
                return;
            }

            throw new InvalidDataException("Unknown invocation identity.");
        }

        if (type == "cancel")
        {
            existing.Cancel();
            return;
        }

        if (type != "callback-result" || !existing.Callbacks.TryRemove(frame.GetProperty("callbackId").GetString()!, out var reply))
        {
            throw new InvalidDataException("Invalid callback identity.");
        }

        if (frame.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
        {
            reply.TrySetException(new InvalidOperationException("Host callback failed: " + error.GetString()));
        }
        else
        {
            reply.TrySetResult(frame.GetProperty("value"));
        }
    }

    private async Task ExecuteAsync(Invocation call, JsonElement request)
    {
        PluginCallContext? context = null;
        object? value = null;
        string? errorCode = null;
        try
        {
            if (request.GetProperty("operation").GetString() != SdkOperations.Call)
            {
                throw new InvalidOperationException("Concurrent workers support unary operations only.");
            }

            context = new PluginCallContext(request.GetProperty("context"), (operation, input, token) => CallbackAsync(call, operation, input, token));
            JsonElement payload = request.GetProperty("payload");
            value = await application.Functions[payload.GetProperty("operation").GetString()!](payload.GetProperty("input"), context, call.Cancellation.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorCode = "sdk-error";
        }

        call.Active = false;
        try
        {
            if (context is not null)
            {
                await context.CompleteAsync();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorCode = "cleanup-error";
        }

        try
        {
            await Task.WhenAll(call.Callbacks.Values.Select(callback => callback.Task)).WaitAsync(call.Cancellation.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorCode ??= "sdk-error";
        }

        await FinishAsync(call, value, errorCode);
    }

    private async Task FinishAsync(Invocation call, object? value, string? errorCode)
    {
        try
        {
            object terminal = errorCode == "cleanup-error" ? new
            {
                type = "error",
                id = call.Id,
                code = errorCode
            }

            : call.Cancellation.IsCancellationRequested ? new
            {
                type = "cancelled",
                id = call.Id
            }

            : errorCode is not null ? new
            {
                type = "error",
                id = call.Id,
                code = errorCode
            }

            : new
            {
                type = "result",
                id = call.Id,
                value,
                reusable = true
            };
            _completed.TryAdd(call.Id, 0);
            _completedOrder.Enqueue(call.Id);
            while (_completedOrder.Count > 4096 && _completedOrder.TryDequeue(out string? expired))
            {
                _completed.TryRemove(expired, out _);
            }

            _calls.TryRemove(call.Id, out _);
            await _channel.WriteAsync(terminal, _lifetime.Token);
            if (errorCode == "cleanup-error")
            {
                await _lifetime.CancelAsync();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await _lifetime.CancelAsync();
        }
        finally
        {
            _calls.TryRemove(call.Id, out _);
            foreach (var callback in call.Callbacks.Values)
            {
                callback.TrySetCanceled();
            }

            call.Dispose();
        }
    }

    private async Task<JsonElement> CallbackAsync(Invocation call, string operation, JsonElement input, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, call.Cancellation.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (!call.Active)
        {
            throw new InvalidOperationException("Expired invocation.");
        }

        string callbackId = Interlocked.Increment(ref call.CallbackSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        call.Callbacks.TryAdd(callbackId, reply);
        await _channel.WriteAsync(new { type = "callback", id = call.Id, callbackId, operation, payload = input }, linked.Token);
        return await reply.Task.WaitAsync(linked.Token);
    }
}
