using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Sdk;
internal sealed partial class ConcurrentRuntime
{
    private const int MaximumRetiredCallbacks = 4096;
    private readonly object _callbackSync = new();
    private readonly HashSet<(string Invocation, string Callback)> _retiredCallbacks = [];
    private readonly Queue<(string Invocation, string Callback)> _retiredCallbackOrder = [];
    private void RouteCallback(JsonElement frame, string id)
    {
        string callbackId = frame.GetProperty(WireFields.CallbackId).GetString() ?? throw new InvalidDataException("Missing callback identity.");
        TaskCompletionSource<JsonElement>? reply;
        lock (_callbackSync)
        {
            reply = null;
            bool found = _calls.TryGetValue(id, out Invocation? call) && call.Callbacks.TryRemove(callbackId, out reply);
            if (!found && _retiredCallbacks.Remove((id, callbackId)))
            {
                return;
            }

            if (!found)
            {
                throw new InvalidDataException("Invalid callback identity.");
            }
        }

        if (frame.TryGetProperty(WireFields.Error, out var error) && error.ValueKind != JsonValueKind.Null)
        {
            reply!.TrySetException(new InvalidOperationException("Host callback failed: " + error.GetString()));
        }
        else
        {
            reply!.TrySetResult(frame.GetProperty(WireFields.Value));
        }
    }

    private void RetireCallbacks(Invocation call)
    {
        TaskCompletionSource<JsonElement>[] pending;
        lock (_callbackSync)
        {
            pending = call.Callbacks.Values.ToArray();
            foreach (string callbackId in call.Callbacks.Keys)
            {
                var identity = (call.Id, callbackId);
                _retiredCallbacks.Add(identity);
                _retiredCallbackOrder.Enqueue(identity);
            }

            call.Callbacks.Clear();
            while (_retiredCallbackOrder.Count > MaximumRetiredCallbacks)
            {
                _retiredCallbacks.Remove(_retiredCallbackOrder.Dequeue());
            }
        }

        foreach (var callback in pending)
        {
            callback.TrySetCanceled();
        }
    }

    private async Task<JsonElement> CallbackAsync(Invocation call, string operation, JsonElement input, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, call.Cancellation.Token);
        string callbackId;
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_callbackSync)
        {
            linked.Token.ThrowIfCancellationRequested();
            if (!call.Active)
            {
                throw new InvalidOperationException("Expired invocation.");
            }

            callbackId = Interlocked.Increment(ref call.CallbackSequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            call.Callbacks.TryAdd(callbackId, reply);
        }

        await _channel.WriteAsync(new { type = FrameKinds.Callback, id = call.Id, callbackId, operation, payload = input }, linked.Token);
        return await reply.Task.WaitAsync(linked.Token);
    }
}
