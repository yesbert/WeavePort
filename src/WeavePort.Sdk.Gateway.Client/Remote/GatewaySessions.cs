using WeavePort.Internal;
using Grpc.Core;
using Grpc.Net.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
// Sessions never cross a RemotePluginClient/binding boundary. Each lease owns one
// reader/writer pair exclusively; only an explicit terminal reply permits reuse.
internal sealed class GatewaySessions : IDisposable
{
    private readonly object _gate = new();
    private readonly Stack<AsyncDuplexStreamingCall<Request, Reply>> _idle = new();
    private readonly SemaphoreSlim _slots = new(8, 8);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly GrpcChannel _channel;
    private readonly WorkerGateway.WorkerGatewayClient _client;
    private readonly Metadata _headers;
    private bool _disposed;
    internal CancellationToken Lifetime { get; }

    internal GatewaySessions(Uri endpoint, Metadata headers, GrpcChannelOptions options)
    {
        Lifetime = _lifetime.Token;
        _headers = headers;
        options.MaxReceiveMessageSize = ProtocolLimits.FrameBytes;
        options.MaxSendMessageSize = ProtocolLimits.FrameBytes;
        _channel = GrpcChannel.ForAddress(endpoint, options);
        _client = new(_channel);
    }

    internal async Task<AsyncDuplexStreamingCall<Request, Reply>> RentAsync(CancellationToken token)
    {
        await _slots.WaitAsync(token);
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _idle.TryPop(out var session) ? session : _client.Session(_headers, cancellationToken: Lifetime);
            }
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    internal void Return(AsyncDuplexStreamingCall<Request, Reply> session, bool complete)
    {
        lock (_gate)
        {
            if (complete && !_disposed)
            {
                _idle.Push(session);
            }
            else
            {
                session.Dispose();
            }
        }

        _slots.Release();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // Do not run cancellation callbacks while holding the pool lock.
        _lifetime.Cancel();
        lock (_gate)
        {
            while (_idle.TryPop(out var session))
            {
                session.Dispose();
            }
        }

        _channel.Dispose();
        _lifetime.Dispose();
    }
}
