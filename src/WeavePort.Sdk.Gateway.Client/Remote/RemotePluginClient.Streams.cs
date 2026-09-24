using System.Runtime.CompilerServices;
using System.Text.Json;
using Grpc.Core;
using WeavePort.Internal;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class RemotePluginClient
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _sessions.Lifetime);
        stop.CancelAfter(_streamOptions.TotalTimeout);
        var request = Request(operation, input);
        request.Mode = Mode.Stream;
        request.StreamId = Guid.NewGuid().ToString("N");
        var session = await _sessions.RentAsync(stop.Token);
        await using var abort = stop.Token.Register(session.Dispose);
        bool complete = false;
        long total = 0;
        try
        {
            await WriteSourceRequestAsync(session, request, stop.Token);
            var buffered = new Queue<JsonElement>();
            while (!complete || buffered.Count != 0)
            {
                stop.Token.ThrowIfCancellationRequested();
                if (buffered.Count == 0)
                {
                    Reply reply = await ReadTranslatedReplyAsync(session, stop.Token);
                    complete = reply.Complete;
                    buffered = ReadBatch(reply, stop.Token);
                    continue;
                }

                JsonElement item = buffered.Dequeue();
                long size = JsonSize.Measure(item);
                total += size;
                if (size > ProtocolLimits.StreamItemBytes || total > ProtocolLimits.StreamTotalBytes)
                {
                    throw new InvalidDataException("Gateway stream limit.");
                }

                yield return item;
            }
        }
        finally
        {
            // Abort an incomplete HTTP/2 stream before asking the server to confirm
            // plugin cleanup. Never reuse a reader that may contain abandoned items.
            await abort.DisposeAsync();
            _sessions.Return(session, complete && !stop.IsCancellationRequested);
            if (!complete && !_sessions.Lifetime.IsCancellationRequested)
            {
                await ExchangeAsync(new Request { Mode = Mode.Cancel, StreamId = request.StreamId }, CancellationToken.None, cleanup: true);
            }
        }
    }

    private static Queue<JsonElement> ReadBatch(Reply reply, CancellationToken token)
    {
        CheckError(reply, token);
        if (reply.Complete)
        {
            return new Queue<JsonElement>();
        }

        return new Queue<JsonElement>(ParseBatch(reply).EnumerateArray());
    }

    private async Task<Reply> ReadTranslatedReplyAsync(AsyncDuplexStreamingCall<Request, Reply> session, CancellationToken token)
    {
        try
        {
            return await ReadStreamReplyAsync(session, token);
        }
        catch (RpcException error)
        {
            throw Translate(error, token);
        }
    }

    private static JsonElement ParseBatch(Reply reply)
    {
        if (reply.Json.Length > ProtocolLimits.StreamBatchBytes)
        {
            throw new InvalidDataException("Gateway batch limit.");
        }

        JsonElement batch = JsonElement.Parse(reply.Json.Span);
        if (batch.ValueKind != JsonValueKind.Array || batch.GetArrayLength() > ProtocolLimits.StreamBatchItems)
        {
            throw new InvalidDataException("Invalid gateway batch shape or count.");
        }

        return batch;
    }
}
