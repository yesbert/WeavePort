using Grpc.Core;
using WeavePort.Abstractions;
using WeavePort.Internal;
using WeavePort.Sdk.Client;

namespace WeavePort.Sdk.Gateway;
public sealed partial class RemotePluginClient
{
    private static Exception Translate(RpcException error, CancellationToken token)
    {
        if (error.StatusCode == StatusCode.Cancelled && token.IsCancellationRequested)
        {
            return new OperationCanceledException("Gateway operation cancelled.", error, token);
        }

        string reported = FailureCode.Normalize(error.Trailers.GetValue(GatewayMetadata.FailureCode));
        string code = reported == FailureCodes.UnknownError ? TransportCode(error.StatusCode) : reported;
        return new PluginCallException(code, error.Trailers.GetValue(GatewayMetadata.MayHaveExecuted) != "false")
        {
            Failure = new PluginFailure(code, FailureCode.Phase(error.Trailers.GetValue(GatewayMetadata.FailurePhase)), FailureCode.Correlation(error.Trailers.GetValue(GatewayMetadata.CorrelationId)), error.Trailers.GetValue(GatewayMetadata.CleanupFailed) == "true")
        };
    }

    private static string TransportCode(StatusCode status) => status switch
    {
        StatusCode.Cancelled => FailureCodes.Cancelled,
        StatusCode.DeadlineExceeded => FailureCodes.Timeout,
        StatusCode.Unauthenticated => FailureCodes.BindingDenied,
        StatusCode.PermissionDenied => FailureCodes.Denied,
        StatusCode.Unavailable => FailureCodes.Unavailable,
        StatusCode.ResourceExhausted => FailureCodes.ResourceExhausted,
        StatusCode.InvalidArgument => FailureCodes.InvalidArgument,
        StatusCode.NotFound => FailureCodes.NotFound,
        StatusCode.Unimplemented => FailureCodes.Unimplemented,
        StatusCode.Internal or StatusCode.DataLoss => FailureCodes.GatewayFailed,
        _ => FailureCodes.UnknownError
    };
}
