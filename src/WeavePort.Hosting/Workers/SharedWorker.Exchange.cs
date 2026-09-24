using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed partial class SharedWorker
{
    private async Task ReadLoopAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await ReadFramesAsync();
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or ObjectDisposedException)
            {
                if (!await RecoverChannelAsync(error))
                {
                    return;
                }
            }
        }
    }

    private async Task ReadFramesAsync()
    {
        while (true)
        {
            JsonElement frame = await _worker!.Reader.ReadAsync(_channelStop!.Token);
            WorkerEnvelope.ValidateExchange(frame);
            string id = frame.GetProperty(WireFields.Id).GetString() ?? "";
            SharedCall call;
            lock (_sync)
            {
                _lastFrame = clock.GetTimestamp();
                if (!_calls.TryGetValue(id, out call!))
                {
                    throw new InvalidDataException("Unknown invocation identity.");
                }
            }

            DispatchFrame(call, frame);
        }
    }

    private async Task<bool> RecoverChannelAsync(Exception primary)
    {
        Retire(_worker);
        try
        {
            await pool.DestroyAsync(_worker!);
        }
        catch (Exception cleanup)
        {
            var combined = new AggregateException("Shared channel and cleanup failed.", primary, cleanup);
            FailPending(combined, true);
            plugin.Disable(cleanup.GetType().Name);
            throw combined;
        }

        FailPending(primary, false);
        if (_lifetime.IsCancellationRequested || !plugin.AllowRestart())
        {
            return false;
        }

        return await ReplaceAsync();
    }

    private void DispatchFrame(SharedCall call, JsonElement frame)
    {
        string type = frame.GetProperty(WireFields.Type).GetString() ?? "";
        if (type == FrameKinds.Callback)
        {
            QueueCallback(call, frame);
            return;
        }

        if (type is not ("result" or "error" or FailureCodes.Cancelled))
        {
            throw new InvalidDataException("Unsupported shared frame.");
        }

        bool cleanupFailed = type == FrameKinds.Error && frame.TryGetProperty(WireFields.Code, out var code) && code.GetString() == FailureCodes.CleanupError;
        if (type == FrameKinds.Error)
        {
            string reported = frame.TryGetProperty(WireFields.PrimaryCode, out var primary) ? primary.GetString()! : frame.TryGetProperty(WireFields.Code, out var reportedCode) ? reportedCode.GetString()! : FailureCodes.Failed;
            call.Failure = new PluginFailure(FailureCode.Normalize(reported), FailurePhases.Exchange, call.Id, cleanupFailed);
            pool.ReportFailure(Instance, call.Failure, new WorkerExecutionException(reported, cleanupFailed));
        }

        JsonElement value = type == FrameKinds.Result ? frame.GetProperty(WireFields.Value) : Empty;
        lock (_sync)
        {
            _calls.Remove(call.Id);
            _active--;
        }

        string status = type switch
        {
            "result" => FailureCodes.Ok,
            FailureCodes.Cancelled => FailureCodes.Cancelled,
            _ => FailureCodes.Failed
        };
        call.Finish(status, Instance, value);
        if (cleanupFailed)
        {
            throw new InvalidDataException("Shared invocation cleanup failed.");
        }
    }

    private void FailPending(Exception error, bool cleanupFailed)
    {
        SharedCall[] calls;
        lock (_sync)
        {
            _ready = false;
            calls = _calls.Values.ToArray();
            _calls.Clear();
            _active -= calls.Length;
        }

        foreach (var call in calls)
        {
            string code = error is InvalidDataException or JsonException ? FailureCodes.ProtocolError : FailureCodes.Failed;
            call.Failure = new PluginFailure(code, FailurePhases.Exchange, call.Id, cleanupFailed);
            pool.ReportFailure(Instance, call.Failure, error);
            call.Finish(FailureCodes.Failed, Instance, Empty);
        }
    }

    private async Task<bool> ReplaceAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            Worker? replacement = null;
            try
            {
                replacement = await pool.StartSharedAsync(plugin.Profile, plugin.Context.Version, _lifetime.Token);
                _worker = replacement;
                await SendAsync(new { type = FrameKinds.Configure, degree = plugin.Options.Degree }, _lifetime.Token, replacement);
                lock (_sync)
                {
                    _channelStop?.Dispose();
                    _channelStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    _ready = true;
                    _lastFrame = clock.GetTimestamp();
                }

                plugin.Host.NotifySharedCapacity();
                return true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or InvalidOperationException)
            {
                await DestroyReplacementAsync(replacement);
                if (_lifetime.IsCancellationRequested || !plugin.AllowRestart())
                {
                    return false;
                }
            }
        }

        return false;
    }

    private void QueueCallback(SharedCall call, JsonElement frame)
    {
        string callbackId = frame.GetProperty(WireFields.CallbackId).GetString() ?? "";
        string operation = frame.GetProperty(WireFields.Operation).GetString() ?? "";
        if (callbackId.Length == 0 || operation.Length == 0)
        {
            throw new InvalidDataException("Missing callback identity or operation.");
        }

        bool allowed;
        lock (_sync)
        {
            if (call.CallbackRejected)
            {
                return;
            }

            allowed = call.Scope.TakeCallback() && call.Scope.Allows(operation);
            if (!allowed)
            {
                call.CallbackRejected = true;
            }
            else if (!call.CallbackIds.Add(callbackId))
            {
                throw new InvalidDataException("Repeated callback identity.");
            }
        }

        _ = RespondAsync(call, frame, _worker!, allowed);
    }

    private async Task RespondAsync(SharedCall call, JsonElement frame, Worker worker, bool allowed)
    {
        try
        {
            string callbackId = frame.GetProperty(WireFields.CallbackId).GetString() ?? "";
            string operation = frame.GetProperty(WireFields.Operation).GetString() ?? "";
            if (callbackId.Length == 0 || operation.Length == 0)
            {
                throw new InvalidDataException("Missing callback identity or operation.");
            }

            CancellationToken channelToken = _channelStop!.Token;
            var(value, error) = await GetCallbackResultAsync(call, operation, frame, allowed, channelToken);
            if (call.Finished || !ReferenceEquals(worker, _worker))
            {
                return;
            }

            await SendAsync(new { type = FrameKinds.CallbackResult, id = call.Id, callbackId, value, error }, channelToken, worker);
        }
        catch (OperationCanceledException)
        {
        // Invocation/channel cancellation revokes the reply. A detached callback
        // still retains its own admission until its actual completion.
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or KeyNotFoundException or JsonException)
        {
            Retire(worker);
        }
    }

    private async Task<(JsonElement Value, string? Error)> GetCallbackResultAsync(SharedCall call, string operation, JsonElement frame, bool allowed, CancellationToken channelToken)
    {
        if (!allowed)
        {
            return (Empty, FailureCodes.Denied);
        }

        try
        {
            JsonElement value = await InvokeCallbackAsync(call, operation, frame.GetProperty(WireFields.Payload), channelToken);
            return (value, null);
        }
        catch (Exception error) when (error is not OperationCanceledException || !(call.Token.IsCancellationRequested || channelToken.IsCancellationRequested))
        {
            return (Empty, FailureCodes.CallbackFailed);
        }
    }

    private async Task<JsonElement> InvokeCallbackAsync(SharedCall call, string operation, JsonElement payload, CancellationToken channelToken)
    {
        if (!await call.Admission.Callbacks.WaitAsync(0, call.Token))
        {
            throw new IOException("Callback capacity exhausted.");
        }

        var stop = CancellationTokenSource.CreateLinkedTokenSource(call.Token, channelToken);
        CancellationToken callbackToken = stop.Token;
        call.Admission.RetainCallback();
        Task<JsonElement> callback = Task.Run(async () =>
        {
            InvocationScope.Current.Value = call.Scope;
            try
            {
                return await plugin.Callbacks.InvokeAsync(new HostCall(call.Context, call.Id, operation, payload, call.Id), callbackToken);
            }
            finally
            {
                InvocationScope.Current.Value = null;
                call.Admission.ReleaseCallback();
                stop.Dispose();
            }
        }, CancellationToken.None);
        try
        {
            return await callback.WaitAsync(callbackToken);
        }
        catch
        {
            _ = callback.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    private async Task DestroyReplacementAsync(Worker? replacement)
    {
        if (replacement is not null)
        {
            await pool.DestroyAsync(replacement);
        }
    }
}
