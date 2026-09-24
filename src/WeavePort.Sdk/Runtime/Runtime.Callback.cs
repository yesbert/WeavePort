using System.Text.Json;
using WeavePort.Internal;

namespace WeavePort.Sdk;

internal sealed partial class Runtime
{
    private async Task WaitForCallbackExchangeAsync(CallScope scope, CancellationToken token)
    {
        while (true)
        {
            await scope.Ready.Task.WaitAsync(token);
            await _callbackGate.WaitAsync(token);
            if (!scope.Active || scope.Ready.Task.IsCompleted && scope.Request.ValueKind != System.Text.Json.JsonValueKind.Undefined)
            {
                return;
            }

            _callbackGate.Release();
        }
    }

    private sealed class CallScope(JsonElement request)
    {
        internal JsonElement Request { get; set; } = request;

        internal bool Active = true;
        internal bool Closing;
        internal TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void PauseCallbacks()
        {
            if (Ready.Task.IsCompleted)
            {
                Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    private async Task<JsonElement> CallbackAsync(string operation, JsonElement input, CancellationToken token)
    {
        CallScope scope = _scope.Value ?? throw new InvalidOperationException("No active invocation.");
        await WaitForCallbackExchangeAsync(scope, token);
        try
        {
            if (!scope.Active)
            {
                throw new InvalidOperationException("Expired invocation.");
            }

            if (scope.Closing)
            {
                throw new OperationCanceledException("Stream is closing.");
            }

            string id = scope.Request.GetProperty(WireFields.Id).GetString()!;
            string callbackId = (++_callback).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _channel.WriteAsync(new
            {
                type = FrameKinds.Callback,
                id,
                callbackId,
                operation,
                payload = input
            }, token);
            JsonElement reply = await _channel.ReadAsync(token) ?? throw new EndOfStreamException();
            if (reply.GetProperty(WireFields.Type).GetString() != FrameKinds.CallbackResult || reply.GetProperty(WireFields.Id).GetString() != id || reply.GetProperty(WireFields.CallbackId).GetString() != callbackId)
            {
                throw new InvalidDataException("Callback identity.");
            }

            if (reply.TryGetProperty(WireFields.Error, out var error) && error.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidOperationException("Host callback failed: " + error.GetString());
            }

            return reply.GetProperty(WireFields.Value);
        }
        finally
        {
            _callbackGate.Release();
        }
    }
}
