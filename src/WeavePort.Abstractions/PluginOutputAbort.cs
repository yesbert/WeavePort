namespace WeavePort.Abstractions;
/// <summary>Why a function output is discarded.</summary>
public enum PluginOutputAbort
{
    /// <summary>The invocation failed: plugin error, worker loss, protocol failure or an incomplete output.</summary>
    Failed,
    /// <summary>The invocation was cancelled or timed out.</summary>
    Cancelled,
    /// <summary>An output or input-read limit was exceeded.</summary>
    Limit,
    /// <summary>Committing another output of the same invocation failed.</summary>
    CommitFailed
}
