using WeavePort.Abstractions;

internal sealed class LocalInvocationException(InvocationResult result) : InvalidOperationException("Local fixture invocation did not succeed.")
{
    // Retain bounded outcome metadata, never fixture payloads or raw worker diagnostics.
    internal object Diagnostic { get; } = new
    {
        result.Status,
        result.Failure,
        result.ElapsedMs,
        result.MayHaveExecuted
    };
}
