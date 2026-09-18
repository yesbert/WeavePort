using Google.Protobuf;
using Grpc.Core;
using WeavePort.Sdk.Gateway.Protocol;

internal sealed class IncompatibleGateway : WorkerGateway.WorkerGatewayBase
{
    public override async Task Session(IAsyncStreamReader<Request> requests, IServerStreamWriter<Reply> replies, ServerCallContext context)
    {
        while (await requests.MoveNext(context.CancellationToken))
            await replies.WriteAsync(new Reply { Complete = true, Json = ByteString.CopyFromUtf8("{\"tenant\":\"A\",\"protocol\":2}") }, context.CancellationToken);
    }
}
