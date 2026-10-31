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
        catch (Exception error) when (token.IsCancellationRequested &&
            (error is ObjectDisposedException || error is RpcException && HasCancelledStatus(session)))
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
        catch (Exception error) when (token.IsCancellationRequested &&
            (error is ObjectDisposedException || error is RpcException && HasCancelledStatus(session)))
        {
            throw new OperationCanceledException("Gateway operation cancelled.", error, token);
        }
    }

    private static bool HasCancelledStatus(AsyncDuplexStreamingCall<Request, Reply> session)
    {
        try
        {
            // An interrupted I/O read can report Unavailable after gRPC has already
            // committed Cancelled. Preserve that authoritative completed call status.
            return session.GetStatus().StatusCode == StatusCode.Cancelled;
        }
        catch (InvalidOperationException)
        {
            // GetStatus is unavailable while a call is still in progress.
            return false;
        }
    }

}
