using System.Collections.Concurrent;
using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;
internal sealed partial class ConcurrentRuntime(PluginApplication application)
{
    private const int MaximumCompletedIdentities = 4096;
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

        internal bool ExecutionFailed;
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
        await channel.WriteAsync(new { type = FrameKinds.Ready, protocol = ProtocolVersions.Concurrent, concurrentCalls = ProtocolVersions.ConcurrentCalls, sessionCleanup = ProtocolVersions.SessionCleanup, pluginVersion = application.PluginVersion }, token);
        JsonElement configure = await channel.ReadAsync(token) ?? throw new EndOfStreamException();
        if (configure.GetProperty(WireFields.Type).GetString() != "configure" || !configure.GetProperty(WireFields.Degree).TryGetInt32(out _degree) || _degree is < 1 or > ProtocolLimits.MaximumConcurrentCalls)
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
        string id = frame.GetProperty(WireFields.Id).GetString() ?? throw new InvalidDataException("Missing invocation identity.");
        string? type = frame.GetProperty(WireFields.Type).GetString();
        if (type == FrameKinds.Invoke)
        {
            Admit(frame, id);
            return;
        }

        if (type == FrameKinds.CallbackResult)
        {
            RouteCallback(frame, id);
            return;
        }

        bool found = _calls.TryGetValue(id, out Invocation? existing);
        if (!found && type == FrameKinds.Cancel && _completed.ContainsKey(id))
        {
            return;
        }

        if (!found)
        {
            throw new InvalidDataException("Unknown invocation identity.");
        }

        if (type != FrameKinds.Cancel)
        {
            throw new InvalidDataException("Invalid concurrent frame.");
        }

        existing!.Cancel();
    }

    private async Task ExecuteAsync(Invocation call, JsonElement request)
    {
        PluginCallContext? context = null;
        object? value = null;
        string? errorCode = null;
        try
        {
            if (request.GetProperty(WireFields.Operation).GetString() != SdkOperations.Call)
            {
                throw new InvalidOperationException("Concurrent workers support unary operations only.");
            }

            context = new PluginCallContext(request.GetProperty(WireFields.Context), (operation, input, token) => CallbackAsync(call, operation, input, token));
            JsonElement payload = request.GetProperty(WireFields.Payload);
            value = await application.Functions[payload.GetProperty(WireFields.Operation).GetString()!](payload.GetProperty(WireFields.Input), context, call.Cancellation.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            call.ExecutionFailed = true;
            errorCode = FailureCodes.SdkError;
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
            errorCode = FailureCodes.CleanupError;
        }

        try
        {
            await Task.WhenAll(call.Callbacks.Values.Select(callback => callback.Task)).WaitAsync(call.Cancellation.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorCode ??= FailureCodes.SdkError;
        }

        await FinishAsync(call, value, errorCode);
    }

    private async Task FinishAsync(Invocation call, object? value, string? errorCode)
    {
        try
        {
            object terminal = CreateTerminal(call, value, errorCode);
            _completed.TryAdd(call.Id, 0);
            _completedOrder.Enqueue(call.Id);
            while (_completedOrder.Count > MaximumCompletedIdentities && _completedOrder.TryDequeue(out string? expired))
            {
                _completed.TryRemove(expired, out _);
            }

            RetireCallbacks(call);
            _calls.TryRemove(call.Id, out _);
            await _channel.WriteAsync(terminal, _lifetime.Token);
            if (errorCode == FailureCodes.CleanupError)
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
            RetireCallbacks(call);
            call.Dispose();
        }
    }

    private static object CreateTerminal(Invocation call, object? value, string? errorCode)
    {
        if (errorCode == FailureCodes.CleanupError)
        {
            return new
            {
                type = FrameKinds.Error,
                id = call.Id,
                code = errorCode,
                primaryCode = call.ExecutionFailed ? FailureCodes.SdkError : errorCode,
                cleanupFailed = errorCode == FailureCodes.CleanupError
            };
        }

        if (call.Cancellation.IsCancellationRequested)
        {
            return new
            {
                type = FrameKinds.Cancelled,
                id = call.Id
            };
        }

        if (errorCode is not null)
        {
            return new
            {
                type = FrameKinds.Error,
                id = call.Id,
                code = errorCode,
                primaryCode = call.ExecutionFailed ? FailureCodes.SdkError : errorCode,
                cleanupFailed = errorCode == FailureCodes.CleanupError
            };
        }

        return new
        {
            type = FrameKinds.Result,
            id = call.Id,
            value,
            reusable = true
        };
    }

    private void Admit(JsonElement frame, string id)
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
        _ = call.Completion.ContinueWith(completedTask => _running.TryRemove(id, out _), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return;
    }
}
