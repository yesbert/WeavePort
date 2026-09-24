using Grpc.Core;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class RemotePluginClient
{
    private static async Task<Reply> ReadAsync(AsyncDuplexStreamingCall<Request, Reply> session, CancellationToken token)
    {
        try
        {
            if (!await session.ResponseStream.MoveNext(token))
            {
                throw new IOException("Gateway session ended without a terminal reply.");
            }

            return session.ResponseStream.Current;
        }
        catch (ObjectDisposedException error) when (token.IsCancellationRequested)
        {
            // Owned cancellation can dispose the gRPC call before its next read starts.
            throw new OperationCanceledException("Gateway operation cancelled.", error, token);
        }
    }

    private static async Task WriteAsync(AsyncDuplexStreamingCall<Request, Reply> session, Request request, CancellationToken token)
    {
        try
        {
            await session.RequestStream.WriteAsync(request, token);
        }
        catch (ObjectDisposedException error) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException("Gateway operation cancelled.", error, token);
        }
    }
}
