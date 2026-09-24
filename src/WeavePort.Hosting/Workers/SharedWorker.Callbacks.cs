using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;

internal sealed partial class SharedWorker
{
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
            var (value, error) = await GetCallbackResultAsync(call, operation, frame, allowed, channelToken);
            if (call.Finished || !ReferenceEquals(worker, _worker))
            {
                return;
            }

            await SendAsync(new
            {
                type = FrameKinds.CallbackResult,
                id = call.Id,
                callbackId,
                value,
                error
            }, channelToken, worker);
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
}
