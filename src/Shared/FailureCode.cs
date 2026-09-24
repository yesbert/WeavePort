namespace WeavePort.Internal;
internal static class FailureCode
{
    internal static string Phase(string? phase) => phase switch
    {
        FailurePhases.Prepare => FailurePhases.Prepare,
        FailurePhases.Exchange => FailurePhases.Exchange,
        _ => FailurePhases.Transport
    };
    internal static string Correlation(string? id) => id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? id : "";
    internal static string Normalize(string? code) => code switch
    {
        FailureCodes.Failed => FailureCodes.Failed,
        FailureCodes.SdkError => FailureCodes.SdkError,
        FailureCodes.CleanupError => FailureCodes.CleanupError,
        FailureCodes.ProtocolError => FailureCodes.ProtocolError,
        FailureCodes.InternalError => FailureCodes.InternalError,
        FailureCodes.Cancelled => FailureCodes.Cancelled,
        FailureCodes.Timeout => FailureCodes.Timeout,
        FailureCodes.Disabled => FailureCodes.Disabled,
        FailureCodes.Denied => FailureCodes.Denied,
        FailureCodes.Busy => FailureCodes.Busy,
        FailureCodes.VersionMismatch => FailureCodes.VersionMismatch,
        FailureCodes.InputLimit => FailureCodes.InputLimit,
        FailureCodes.ValueLimit => FailureCodes.ValueLimit,
        FailureCodes.GatewayFailed => FailureCodes.GatewayFailed,
        FailureCodes.BindingDenied => FailureCodes.BindingDenied,
        FailureCodes.CallbackFailed => FailureCodes.CallbackFailed,
        FailureCodes.Unavailable => FailureCodes.Unavailable,
        FailureCodes.ResourceExhausted => FailureCodes.ResourceExhausted,
        FailureCodes.InvalidArgument => FailureCodes.InvalidArgument,
        FailureCodes.NotFound => FailureCodes.NotFound,
        FailureCodes.Unimplemented => FailureCodes.Unimplemented,
        FailureCodes.UnknownError => FailureCodes.UnknownError,
        FailureCodes.ChunkLimit => FailureCodes.ChunkLimit,
        FailureCodes.SourceUnsupported => FailureCodes.SourceUnsupported,
        FailureCodes.InvalidMode => FailureCodes.InvalidMode,
        FailureCodes.StreamLimit => FailureCodes.StreamLimit,
        _ => FailureCodes.UnknownError
    };
}
