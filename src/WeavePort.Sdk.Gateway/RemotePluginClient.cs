using WeavePort.Internal;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
/// <summary>Product client for a preconfigured worker-host binding; the plugin artifact stays unchanged.</summary>
public sealed class RemotePluginClient : IPluginClient
{
    private readonly GatewaySessions _sessions;
    private readonly TimeSpan _callTimeout;
    /// <summary>Creates a binding client with at most eight leased sessions and a default 30-second operation timeout, including lease wait. Plain HTTP is permitted only on loopback; remote endpoints require HTTPS.</summary>
    public RemotePluginClient(Uri endpoint, string bindingCredential, TimeSpan? callTimeout = null)
    {
        if (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback))
        {
            throw new ArgumentException("Remote gateway requires HTTPS.", nameof(endpoint));
        }

        _callTimeout = callTimeout ?? TimeSpan.FromSeconds(30);
        if (_callTimeout <= TimeSpan.Zero || _callTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(callTimeout));
        }

        _sessions = new(endpoint, new() { { "x-weaveport-binding", bindingCredential } });
    }

    /// <inheritdoc/>
    public Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => ExchangeAsync(Request(operation, input), cancellationToken);
    private async Task<JsonElement> ExchangeAsync(Request request, CancellationToken cancellationToken, bool cleanup = false)
    {
        using var stop = cleanup ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessions.Lifetime);
        stop.CancelAfter(cleanup ? TimeSpan.FromSeconds(5) : _callTimeout);
        var session = await _sessions.RentAsync(stop.Token);
        bool complete = false;
        try
        {
            await session.RequestStream.WriteAsync(request, stop.Token);
            Reply reply = await ReadAsync(session, stop.Token);
            complete = reply.Complete;
            if (!complete)
            {
                throw new InvalidDataException("Missing gateway terminal reply.");
            }

            CheckError(reply, stop.Token);
            if (reply.Json.Length > 512 << 10)
            {
                throw new PluginCallException("value-limit");
            }

            return JsonElement.Parse(reply.Json.Span);
        }
        catch (RpcException error)
        {
            throw Translate(error, stop.Token);
        }
        finally
        {
            _sessions.Return(session, complete);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessions.Lifetime);
        stop.CancelAfter(_callTimeout);
        var request = Request(operation, input);
        request.Mode = Mode.Stream;
        request.StreamId = Guid.NewGuid().ToString("N");
        var session = await _sessions.RentAsync(stop.Token);
        bool complete = false;
        long total = 0;
        try
        {
            try
            {
                await session.RequestStream.WriteAsync(request, stop.Token);
            }
            catch (RpcException error)
            {
                throw Translate(error, stop.Token);
            }

            while (true)
            {
                Reply reply;
                try
                {
                    reply = await ReadAsync(session, stop.Token);
                }
                catch (RpcException error)
                {
                    throw Translate(error, stop.Token);
                }

                complete = reply.Complete;
                CheckError(reply, stop.Token);
                if (complete)
                {
                    yield break;
                }

                JsonElement batch = ParseBatch(reply);
                foreach (JsonElement item in batch.EnumerateArray())
                {
                    stop.Token.ThrowIfCancellationRequested();
                    long size = JsonSize.Measure(item);
                    total += size;
                    if (size > 128 << 10 || total > 64 << 20)
                    {
                        throw new InvalidDataException("Gateway stream limit.");
                    }

                    yield return item;
                }
            }
        }
        finally
        {
            // Abort an incomplete HTTP/2 stream before asking the server to confirm
            // plugin cleanup. Never reuse a reader that may contain abandoned items.
            _sessions.Return(session, complete);
            if (!complete && !_sessions.Lifetime.IsCancellationRequested)
            {
                await ExchangeAsync(new Request { Mode = Mode.Cancel, StreamId = request.StreamId }, CancellationToken.None, cleanup: true);
            }
        }
    }

    private static JsonElement ParseBatch(Reply reply)
    {
        if (reply.Json.Length > 256 << 10)
        {
            throw new InvalidDataException("Gateway batch limit.");
        }

        JsonElement batch = JsonElement.Parse(reply.Json.Span);
        if (batch.GetArrayLength() > 16)
        {
            throw new InvalidDataException("Gateway batch count.");
        }

        return batch;
    }

    private static async Task<Reply> ReadAsync(AsyncDuplexStreamingCall<Request, Reply> session, CancellationToken token)
    {
        if (!await session.ResponseStream.MoveNext(token))
        {
            throw new IOException("Gateway session ended without a terminal reply.");
        }

        return session.ResponseStream.Current;
    }

    private static void CheckError(Reply reply, CancellationToken token)
    {
        if (reply.Cancelled)
        {
            throw new OperationCanceledException("Gateway call cancelled.", token);
        }

        if (reply.Error.Length > 0)
        {
            throw new PluginCallException(reply.Error, reply.MayHaveExecuted);
        }
    }

    private static Request Request(string operation, JsonElement input)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(input);
        if (json.Length > 512 << 10)
        {
            throw new PluginCallException("input-limit", false);
        }

        return new()
        {
            Operation = operation,
            // Transfer the fresh serializer array; no mutable alias survives this method.
            Input = UnsafeByteOperations.UnsafeWrap(json)
        };
    }

    private static Exception Translate(RpcException error, CancellationToken token) => error.StatusCode == StatusCode.Cancelled ? new OperationCanceledException("Gateway call cancelled.", error, token) : new PluginCallException(error.Status.Detail, error.Trailers.GetValue("may-have-executed") != "false");
    /// <summary>Cancels outstanding calls and disposes binding-scoped transport sessions; server binding lifetime remains owned by the trusted worker-host application.</summary>
    public ValueTask DisposeAsync()
    {
        _sessions.Dispose();
        return ValueTask.CompletedTask;
    }
}
