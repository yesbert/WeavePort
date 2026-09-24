using System.Text.Json;

namespace WeavePort.Abstractions;
/// <summary>An observable result of a bounded invocation.</summary>
/// <param name = "Status">ok, busy, disabled, cancelled, timeout, failed, denied, protocol-error, or version-mismatch.</param>
/// <param name = "Value">The contract-specific output.</param>
/// <param name = "Instance">Execution instance identifier.</param>
/// <param name = "ElapsedMs">Monotonic invocation duration including transport.</param>
/// <param name = "MayHaveExecuted">True once dispatch begins; failed or timed-out calls may already have caused effects.</param>
public sealed record InvocationResult(string Status, JsonElement Value, string Instance, double ElapsedMs, bool MayHaveExecuted = false)
{
    /// <summary>Safe structured failure information when supplied by the execution boundary.</summary>
    public WeavePort.Abstractions.PluginFailure? Failure { get; init; }
    /// <summary>Expected and advertised artifact versions when startup rejected a mismatched worker.</summary>
    public PluginVersionMismatch? VersionMismatch { get; init; }
}
