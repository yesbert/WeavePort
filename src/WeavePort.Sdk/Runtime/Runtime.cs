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
    internal async Task RunAsync(CancellationToken token)
    {
        await using var channel = new Channel();
        _channel = channel;
        await channel.WriteAsync(new
        {
            type = FrameKinds.Ready,
            protocol = ProtocolVersions.Exclusive,
            pluginVersion = application.PluginVersion,
            sessionCleanup = ProtocolVersions.SessionCleanup
        }, token);
        try
        {
            while (await channel.ReadAsync(token) is { } request)
            {
                _request = request;
                var scope = _sessionScope ?? new CallScope(request);
                scope.Request = request;
                scope.Closing = request.GetProperty(WireFields.Operation).GetString() is SdkOperations.Close or SdkOperations.SourceClose;
                scope.Ready.TrySetResult();
                _scope.Value = scope;
                try
                {
                    object result = await DispatchAsync(request.GetProperty(WireFields.Operation).GetString()!, request.GetProperty(WireFields.Payload), token);
                    await WriteResultAsync(scope, request, result, token);
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

    private async Task WriteResultAsync(CallScope scope, JsonElement request, object result, CancellationToken token)
    {
        await _callbackGate.WaitAsync(token);
        try
        {
            scope.Request = default;
            scope.PauseCallbacks();
            await _channel.WriteAsync(new
            {
                type = FrameKinds.Result,
                id = request.GetProperty(WireFields.Id).GetString(),
                value = result,
                reusable = _context is null && _enumerator is null && _source is null
            }, token);
        }
        finally
        {
            _callbackGate.Release();
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


}
