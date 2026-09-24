using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Sdk;
internal sealed partial class Runtime(PluginApplication application)
{
    private Channel _channel = null!;
    private JsonElement _request;
    private PluginCallContext? _context;
    private IAsyncEnumerator<JsonElement>? _enumerator;
    private CancellationTokenSource? _streamCancellation;
    private JsonElement? _pending;
    private Task<bool>? _advancement;
    private string? _streamId;
    private long _bytes;
    private int _callback;
    private readonly SemaphoreSlim _callbackGate = new(1);
    private readonly AsyncLocal<CallScope?> _scope = new();
    private CallScope? _sessionScope;
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

    internal async Task RunAsync(CancellationToken token)
    {
        await using var channel = new Channel();
        _channel = channel;
        await channel.WriteAsync(new { type = FrameKinds.Ready, protocol = ProtocolVersions.Exclusive, pluginVersion = application.PluginVersion, sessionCleanup = ProtocolVersions.SessionCleanup }, token);
        try
        {
            while (await channel.ReadAsync(token)is { } request)
            {
                _request = request;
                var scope = _sessionScope ?? new CallScope(request);
                scope.Request = request;
                scope.Closing = request.GetProperty(WireFields.Operation).GetString()is SdkOperations.Close or SdkOperations.SourceClose;
                scope.Ready.TrySetResult();
                _scope.Value = scope;
                try
                {
                    object result = await DispatchAsync(request.GetProperty(WireFields.Operation).GetString()!, request.GetProperty(WireFields.Payload), token);
                    await _callbackGate.WaitAsync(token);
                    try
                    {
                        scope.Request = default;
                        scope.PauseCallbacks();
                        await channel.WriteAsync(new { type = FrameKinds.Result, id = request.GetProperty(WireFields.Id).GetString(), value = result, reusable = _context is null && _enumerator is null && _source is null }, token);
                    }
                    finally
                    {
                        _callbackGate.Release();
                    }
                }
                catch (Exception error) when (error is not OutOfMemoryException && !token.IsCancellationRequested)
                {
                    await ReportFailureAsync(request, error, token);
                }
                finally
                {
                    scope.Active = _sessionScope == scope;
                    scope.Request = default;
                    _scope.Value = null;
                    _request = default;
                    request = default;
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
        if (operation is SdkOperations.SourceOpen or SdkOperations.SourceRead or SdkOperations.SourceClose)
        {
            return await DispatchSourceAsync(operation, payload, token);
        }

        if (operation is SdkOperations.Next or SdkOperations.Close)
        {
            return await DispatchStreamAsync(operation, payload);
        }

        if (_enumerator is not null || _source is not null)
        {
            throw new InvalidOperationException("Stream already active.");
        }

        string name = payload.GetProperty(WireFields.Operation).GetString()!;
        JsonElement input = payload.GetProperty(WireFields.Input);
        _sessionScope = _scope.Value;
        var context = _context = new PluginCallContext(_request.GetProperty(WireFields.Context), CallbackAsync);
        if (operation == SdkOperations.Call)
        {
            return await SessionCleanup.ExecuteAsync(async () => await application.Functions[name](input, context, token), CompleteContextAsync);
        }

        if (operation != SdkOperations.Start)
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
            await _channel.WriteAsync(new { type = FrameKinds.Callback, id, callbackId, operation, payload = input }, token);
            JsonElement reply = await _channel.ReadAsync(token) ?? throw new EndOfStreamException();
            if (reply.GetProperty(WireFields.Type).GetString() != "callback-result" || reply.GetProperty(WireFields.Id).GetString() != id || reply.GetProperty(WireFields.CallbackId).GetString() != callbackId)
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

    private async Task CloseAsync()
    {
        var enumerator = _enumerator;
        _enumerator = null;
        _streamId = null;
        _pending = null;
        var cancellation = _streamCancellation;
        try
        {
            await SessionCleanup.RunAsync(() => cancellation?.CancelAsync() ?? Task.CompletedTask, () => enumerator is null ? Task.CompletedTask : CompleteEnumeratorAsync(enumerator), CloseSourceAsync, CompleteContextAsync);
        }
        finally
        {
            cancellation?.Dispose();
            _streamCancellation = null;
        }
    }

    private async Task<object> DispatchStreamAsync(string operation, JsonElement payload)
    {
        if (_streamId is null || payload.GetProperty(WireFields.Stream).GetString() != _streamId)
        {
            throw new InvalidDataException("Stream ownership.");
        }

        if (operation == SdkOperations.Close)
        {
            await CloseAsync();
            return new
            {
            };
        }

        return await NextAsync();
    }

    private async Task CompleteEnumeratorAsync(IAsyncEnumerator<JsonElement> enumerator)
    {
        await SessionCleanup.RunAsync(CompleteAdvancementAsync, () => DisposeEnumeratorAsync(enumerator));
    }

    private async Task CompleteAdvancementAsync()
    {
        Task<bool>? advancement = _advancement;
        _advancement = null;
        try
        {
            if (advancement is not null)
            {
                await advancement;
            }
        }
        catch (OperationCanceledException) when (_streamCancellation?.IsCancellationRequested == true)
        {
        // The owned stream token confirms cancellation of this advancement.
        }
    }
}
