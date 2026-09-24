namespace WeavePort.Abstractions;
/// <summary>Safe failure identity. Contains no exception text or plugin payload.</summary>
/// <param name = "Code">Stable error code; unknown peer errors use a controlled fallback.</param>
/// <param name = "Phase">Failure boundary such as prepare, exchange or transport.</param>
/// <param name = "CorrelationId">Opaque invocation identity used to correlate diagnostics.</param>
/// <param name = "CleanupFailed">Whether releasing owned resources also failed.</param>
public sealed record PluginFailure(string Code, string Phase, string CorrelationId, bool CleanupFailed = false);
