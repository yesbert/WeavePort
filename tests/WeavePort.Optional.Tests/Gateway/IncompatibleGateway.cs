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
        {
            string credential = context.RequestHeaders.GetValue("x-weaveport-binding") ?? "";
            if (credential.StartsWith("batch-", StringComparison.Ordinal))
            {
                string batch = MalformedBatch(credential);
                bool stream = requests.Current.Mode == Mode.Stream;
                string value = stream ? batch : "{\"tenant\":\"A\",\"protocol\":1}";
                await replies.WriteAsync(new Reply { Complete = !stream, Json = ByteString.CopyFromUtf8(value) }, context.CancellationToken);
                continue;
            }
            await replies.WriteAsync(new Reply { Complete = true, Json = ByteString.CopyFromUtf8(json) }, context.CancellationToken);
        }
    }
    private static string MalformedBatch(string credential)
    {
        return credential switch
        {
            "batch-object" => "{}",
            "batch-null" => "null",
            "batch-string" => "\"wrong\"",
            "batch-number" => "7",
            "batch-count" => "[" + string.Join(",", Enumerable.Repeat("0", 17)) + "]",
            "batch-item" => "[\"" + new string('x', 128 * 1024) + "\"]",
            _ => "[]"
        };
    }

}
