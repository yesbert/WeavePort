using WeavePort.Internal;
using Google.Protobuf;
using Grpc.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class GatewayService
{
    private async Task SourceAsync(Request request, IServerStreamWriter<Reply> output, ServerCallContext context)
    {
        if (request.ChunkBytes is < ProtocolLimits.SourceChunkMinimumBytes or > ProtocolLimits.SourceChunkMaximumBytes)
        {
            throw new PluginCallException(FailureCodes.ChunkLimit, false);
        }

        string credential = context.RequestHeaders.GetValue(GatewayMetadata.BindingCredential) ?? "";
        if (Authorize(context) is not IBoundPluginClient client)
        {
            throw new PluginCallException(FailureCodes.SourceUnsupported, false);
        }

        var active = registry.Begin(credential, request.StreamId, context.CancellationToken);
        try
        {
            CancellationToken token = active.Stop.Token;
            await foreach (ReadOnlyMemory<byte> block in client.SourceAsync(request.Operation, Decode(request), request.ChunkBytes, token))
            {
                if (block.Length > request.ChunkBytes)
                {
                    throw new PluginCallException(FailureCodes.ChunkLimit);
                }

                // Copy because the producer may reuse its block after MoveNext.
                await output.WriteAsync(new Reply { Data = ByteString.CopyFrom(block.Span) }, token);
            }
        }
        finally
        {
            registry.End(credential, active);
        }
    }
}
