using WeavePort.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WeavePort.Sdk.Client;
/// <summary>Failed SDK operation; failure does not imply that external effects did not occur.</summary>
public sealed class PluginCallException(string status, bool mayHaveExecuted = true) : IOException("Plugin operation: " + status)
{
    /// <summary>Safe structured failure information when supplied by the execution boundary.</summary>
    public WeavePort.Abstractions.PluginFailure? Failure { get; init; }
    /// <summary>Expected and advertised artifact versions when startup failed before dispatch.</summary>
    public PluginVersionMismatch? VersionMismatch { get; init; }
    /// <summary>Host or SDK failure classification.</summary>
    public string Status { get; } = status;
    /// <summary>Gets the stable operation status code; unknown peer codes are preserved.</summary>
    public string ErrorCode => Status;
    /// <summary>Whether dispatch may already have caused effects.</summary>
    public bool MayHaveExecuted { get; } = mayHaveExecuted;
}
