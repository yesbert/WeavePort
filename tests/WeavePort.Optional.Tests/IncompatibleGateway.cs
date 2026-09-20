using Google.Protobuf;
using Grpc.Core;
using WeavePort.Sdk.Gateway.Protocol;

internal sealed class IncompatibleGateway : WorkerGateway.WorkerGatewayBase
{
    public override async Task Session(IAsyncStreamReader<Request> requests, IServerStreamWriter<Reply> replies, ServerCallContext context)
    {
        string json = context.RequestHeaders.GetValue("x-weaveport-binding") switch
        {
            "array" => "[]",
            "null" => "null",
            "string-version" => "{\"tenant\":\"A\",\"protocol\":\"1\"}",
            "null-version" => "{\"tenant\":\"A\",\"protocol\":null}",
            "missing-tenant" => "{\"protocol\":1}",
            "numeric-tenant" => "{\"tenant\":7,\"protocol\":1}",
            "blank-tenant" => """{"tenant":" ","protocol":1}""",
            _ => "{\"tenant\":\"A\",\"protocol\":2}"
        };
        while (await requests.MoveNext(context.CancellationToken))
            await replies.WriteAsync(new Reply { Complete = true, Json = ByteString.CopyFromUtf8(json) }, context.CancellationToken);
    }
}
