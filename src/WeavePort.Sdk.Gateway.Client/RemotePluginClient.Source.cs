using System.Runtime.CompilerServices;
using System.Text.Json;
using Grpc.Core;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
public sealed partial class RemotePluginClient
{
    /// <inheritdoc/>
    public IAsyncEnumerable<ReadOnlyMemory<byte>> SourceAsync(string operation, JsonElement input, int chunkBytes = 65536, CancellationToken cancellationToken = default)
    {
        if (chunkBytes is < 4096 or > 262144)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkBytes));
        }

        return ReadSourceAsync(operation, input, chunkBytes, cancellationToken);
    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadSourceAsync(string operation, JsonElement input, int chunkBytes, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessions.Lifetime);
        stop.CancelAfter(_streamOptions.TotalTimeout);
        var request = Request(operation, input);
        request.Mode = Mode.Source;
        request.StreamId = Guid.NewGuid().ToString("N");
        request.ChunkBytes = chunkBytes;
        var session = await _sessions.RentAsync(stop.Token);
        await using var abort = stop.Token.Register(session.Dispose);
        bool complete = false;
        try
        {
            await WriteSourceRequestAsync(session, request, stop.Token);
            while (true)
            {
                Reply reply;
                try
                {
                    reply = await ReadStreamReplyAsync(session, stop.Token);
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

                if (reply.Data.Length > chunkBytes)
                {
                    throw new InvalidDataException("Gateway source chunk limit.");
                }

                yield return reply.Data.Memory;
            }
        }
        finally
        {
            await abort.DisposeAsync();
            _sessions.Return(session, complete && !stop.IsCancellationRequested);
            if (!complete && !_sessions.Lifetime.IsCancellationRequested)
            {
                await ExchangeAsync(new Request { Mode = Mode.Cancel, StreamId = request.StreamId }, CancellationToken.None, cleanup: true);
            }
        }
    }

    private async Task WriteSourceRequestAsync(AsyncDuplexStreamingCall<Request, Reply> session, Request request, CancellationToken token)
    {
        try
        {
            using var exchange = CancellationTokenSource.CreateLinkedTokenSource(token);
            exchange.CancelAfter(_streamOptions.ExchangeTimeout);
            await session.RequestStream.WriteAsync(request, exchange.Token);
        }
        catch (RpcException error)
        {
            throw Translate(error, token);
        }
    }
}
