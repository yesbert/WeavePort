using Grpc.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;
public sealed partial class RemotePluginClient
{
    private static void ValidateStreamOptions(PluginStreamOptions options)
    {
        foreach (TimeSpan timeout in new[]
        {
            options.ExchangeTimeout,
            options.TotalTimeout
        }

        )
        {
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
        }
    }

    private async Task<Reply> ReadStreamReplyAsync(AsyncDuplexStreamingCall<Request, Reply> session, CancellationToken token)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(token);
        exchange.CancelAfter(_streamOptions.ExchangeTimeout);
        try
        {
            return await ReadAsync(session, exchange.Token);
        }
        catch (Exception error) when (exchange.IsCancellationRequested && !token.IsCancellationRequested && error is OperationCanceledException or RpcException)
        {
            throw new PluginCallException(WeavePort.Internal.FailureCodes.Timeout);
        }
    }
}
