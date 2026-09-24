using Grpc.Core;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class RemotePluginClient
{
    private const double MaximumTimeoutMilliseconds = uint.MaxValue - 1;
    private static void ValidateStreamOptions(PluginStreamOptions options)
    {
        if (!IsSupportedTimeout(options.ExchangeTimeout) || !IsSupportedTimeout(options.TotalTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    private static bool IsSupportedTimeout(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout.TotalMilliseconds <= MaximumTimeoutMilliseconds;
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
