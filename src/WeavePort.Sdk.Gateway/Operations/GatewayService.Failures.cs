using Grpc.Core;
using WeavePort.Internal;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway.Protocol;

namespace WeavePort.Sdk.Gateway;

public sealed partial class GatewayService
{
    private static Reply EncodeFailure(Exception error)
    {
        RpcException mapped = error as RpcException ?? Map(error);
        var terminal = new Reply
        {
            Error = error is PluginCallException legacy
                ? FailureCode.Normalize(legacy.Status)
                : FailureCode.Normalize(mapped.Trailers.GetValue(GatewayMetadata.FailureCode)),
            FailureCode = FailureCode.Normalize(mapped.Trailers.GetValue(GatewayMetadata.FailureCode)),
            FailurePhase = mapped.Trailers.GetValue(GatewayMetadata.FailurePhase) ?? FailurePhases.Transport,
            CorrelationId = mapped.Trailers.GetValue(GatewayMetadata.CorrelationId) ?? "",
            CleanupFailed = mapped.Trailers.GetValue(GatewayMetadata.CleanupFailed) == "true",
            MayHaveExecuted = mapped.StatusCode != StatusCode.Unauthenticated
                && mapped.Trailers.GetValue(GatewayMetadata.MayHaveExecuted) != "false",
            Cancelled = mapped.StatusCode == StatusCode.Cancelled
        };
        IncludeVersionMismatch(terminal, error);
        return terminal;
    }

    private static RpcException Map(Exception error)
    {
        if (error is RpcException rpc)
        {
            return rpc;
        }

        var call = error as PluginCallException;
        string code = error switch
        {
            UnauthorizedAccessException => FailureCodes.BindingDenied,
            OperationCanceledException => FailureCodes.Cancelled,
            PluginCallException => FailureCode.Normalize(call!.Failure?.Code ?? call.Status),
            _ => FailureCodes.GatewayFailed
        };
        StatusCode status = error switch
        {
            UnauthorizedAccessException => StatusCode.Unauthenticated,
            OperationCanceledException => StatusCode.Cancelled,
            PluginCallException => StatusCode.FailedPrecondition,
            _ => StatusCode.Internal
        };
        var trailers = new Metadata
        {
            {
                GatewayMetadata.FailureCode,
                code
            },
            {
                GatewayMetadata.FailurePhase,
                call?.Failure?.Phase ?? FailurePhases.Transport
            },
            {
                GatewayMetadata.CorrelationId,
                call?.Failure?.CorrelationId ?? ""
            },
            {
                GatewayMetadata.CleanupFailed,
                call?.Failure?.CleanupFailed == true ? "true" : "false"
            },
            {
                GatewayMetadata.MayHaveExecuted,
                call?.MayHaveExecuted == false || error is UnauthorizedAccessException ? "false" : "true"
            }
        };
        return new RpcException(new Status(status, code), trailers);
    }

    private static void IncludeVersionMismatch(Reply terminal, Exception error)
    {
        if (error is not PluginCallException { VersionMismatch: { } mismatch })
        {
            return;
        }

        terminal.ExpectedVersion = mismatch.Expected;
        terminal.AdvertisedVersion = mismatch.Advertised;
    }
}
