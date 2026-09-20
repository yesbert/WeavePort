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
            WorkerEnvelope.Validate(frame);
            if (frame.GetProperty("id").GetString() != id)
            {
                throw new InvalidDataException("Invocation mismatch.");
            }

            string? type = frame.GetProperty("type").GetString();
            if (type == "result")
            {
                ReadCleanupAcknowledgement(frame);
                return frame.GetProperty("value");
            }

            if (type == "error")
            {
                if (frame.TryGetProperty("code", out JsonElement code) && code.GetString() == "cleanup-error")
                {
                    pool.RecordCleanupFailure();
                }

                throw new IOException("Plugin reported an execution error.");
            }

            if (type != "callback")
            {
                throw new InvalidDataException("Unknown frame.");
            }

            string operation = frame.GetProperty("operation").GetString() ?? "";
            string callbackId = frame.GetProperty("callbackId").GetString() ?? "";
            if (!binding.Grants.Contains(operation) || !(InvocationScope.Current.Value?.Allows(operation) ?? false))
            {
                throw new UnauthorizedAccessException();
            }

            if (!(InvocationScope.Current.Value?.TakeCallback() ?? false) || !callbackIds.Add(callbackId))
            {
                throw new InvalidDataException("Callback budget or identity violation.");
            }

            var call = new HostCall(binding.Context, id, operation, frame.GetProperty("payload"), trace);
            JsonElement value = await InvokeCallbackAsync(call, token);
            await Frames.WriteAsync(_worker!.Input, new CallbackResultFrame("callback-result", id, callbackId, value), WireJson.Default.CallbackResultFrame, token);
        }
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
}
