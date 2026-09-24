using WeavePort.Internal;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed partial class PluginSession
{
    private async Task<JsonElement> ExchangeAsync(string id, string trace, CancellationToken token)
    {
        var callbackIds = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            JsonElement frame = await _worker!.Reader.ReadAsync(token);
            WorkerEnvelope.ValidateExchange(frame);
            if (frame.GetProperty(WireFields.Id).GetString() != id)
            {
                throw new InvalidDataException("Invocation mismatch.");
            }

            string? type = frame.GetProperty(WireFields.Type).GetString();
            if (type == FrameKinds.Result)
            {
                ReadCleanupAcknowledgement(frame);
                return frame.GetProperty(WireFields.Value);
            }

            if (type == FrameKinds.Error)
            {
                throw ExecutionError(frame);
            }

            if (type != FrameKinds.Callback)
            {
                throw new InvalidDataException("Unknown frame.");
            }

            await RespondToCallbackAsync(frame, id, trace, callbackIds, token);
        }
    }

    private async Task RespondToCallbackAsync(JsonElement frame, string id, string trace, HashSet<string> callbackIds, CancellationToken token)
    {
        string operation = frame.GetProperty(WireFields.Operation).GetString() ?? "";
        string callbackId = frame.GetProperty(WireFields.CallbackId).GetString() ?? "";
        if (!binding.Grants.Contains(operation) || !(InvocationScope.Current.Value?.Allows(operation) ?? false))
        {
            throw new UnauthorizedAccessException();
        }

        if (!(InvocationScope.Current.Value?.TakeCallback() ?? false) || !callbackIds.Add(callbackId))
        {
            throw new InvalidDataException("Callback budget or identity violation.");
        }

        var call = new HostCall(binding.Context, id, operation, frame.GetProperty(WireFields.Payload), trace);
        JsonElement value = await InvokeCallbackAsync(call, token);
        await Frames.WriteAsync(_worker!.Input, new CallbackResultFrame("callback-result", id, callbackId, value), WireJson.Default.CallbackResultFrame, token);
    }

    private async Task<JsonElement> InvokeCallbackAsync(HostCall call, CancellationToken token)
    {
        if (!await admission.Callbacks.WaitAsync(0, token))
        {
            throw new IOException("Callback capacity exhausted.");
        }

        admission.RetainCallback();
        Task<JsonElement> callback = Task.Run(async () =>
        {
            try
            {
                return await binding.Callbacks.InvokeAsync(call, token);
            }
            catch (Exception error) when (error is not OperationCanceledException and not UnauthorizedAccessException)
            {
                RuntimeLog.CallbackFailed(logger, Instance, call.InvocationId, error.GetType().Name);
                throw new IOException("Host callback failed.", error);
            }
            finally
            {
                admission.ReleaseCallback();
            }
        }, CancellationToken.None);
        JsonElement value;
        try
        {
            value = await callback.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            _ = callback.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }

        return value;
    }

    private IOException ExecutionError(JsonElement frame)
    {
        if (frame.TryGetProperty(WireFields.Code, out JsonElement code) && code.GetString() == FailureCodes.CleanupError)
        {
            pool.RecordCleanupFailure();
        }

        string failureCode = frame.TryGetProperty(WireFields.Code, out var reported) && reported.ValueKind == JsonValueKind.String ? reported.GetString()! : FailureCodes.Failed;
        bool cleanupFailed = failureCode == FailureCodes.CleanupError || (frame.TryGetProperty(WireFields.CleanupFailed, out var cleanup) && cleanup.ValueKind == JsonValueKind.True);
        string primaryCode = frame.TryGetProperty(WireFields.PrimaryCode, out var primary) && primary.ValueKind == JsonValueKind.String ? primary.GetString()! : failureCode;
        return new WorkerExecutionException(primaryCode, cleanupFailed);
    }
}
