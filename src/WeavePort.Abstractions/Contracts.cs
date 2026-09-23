using System.Text.Json;

namespace WeavePort.Abstractions;
/// <summary>Host-bound identity and immutable invocation configuration.</summary>
/// <param name = "Tenant">Authenticated data owner, supplied by the trusted host.</param>
/// <param name = "Plugin">Plugin identifier.</param>
/// <param name = "Version">Resolved plugin version.</param>
/// <param name = "Profile">Configuration profile identifier.</param>
/// <param name = "Configuration">Configuration for this binding, including scoped test secrets.</param>
public sealed record PluginContext(string Tenant, string Plugin, string Version, string Profile, JsonElement Configuration);
/// <summary>An observable result of a bounded invocation.</summary>
/// <param name = "Status">ok, busy, disabled, cancelled, timeout, failed, denied, protocol-error, or version-mismatch.</param>
/// <param name = "Value">The contract-specific output.</param>
/// <param name = "Instance">Execution instance identifier.</param>
/// <param name = "ElapsedMs">Monotonic invocation duration including transport.</param>
/// <param name = "MayHaveExecuted">True once dispatch begins; failed or timed-out calls may already have caused effects.</param>
public sealed record InvocationResult(string Status, JsonElement Value, string Instance, double ElapsedMs, bool MayHaveExecuted = false)
{
    /// <summary>Expected and advertised artifact versions when startup rejected a mismatched worker.</summary>
    public PluginVersionMismatch? VersionMismatch { get; init; }
}

/// <summary>Artifact identity disagreement observed before plugin dispatch.</summary>
/// <param name = "Expected">The host binding's required artifact version.</param>
/// <param name = "Advertised">The worker's advertised artifact version.</param>
public sealed record PluginVersionMismatch(string Expected, string Advertised);
/// <summary>A host-authorized callback. Tenant identity is never taken from plugin data.</summary>
/// <param name = "Context">The binding's trusted authority.</param>
/// <param name = "InvocationId">The host-generated invocation identifier.</param>
/// <param name = "Operation">Granted callback name.</param>
/// <param name = "Payload">Untrusted plugin arguments.</param>
/// <param name = "TraceId">Host trace correlation.</param>
public sealed record HostCall(PluginContext Context, string InvocationId, string Operation, JsonElement Payload, string TraceId);
/// <summary>Dispatches an authorized callback; implementations must observe cancellation.</summary>
public interface IHostCallbacks
{
    /// <summary>Executes a callback with host-owned identity and a bounded lifetime.</summary>
    ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken);
}

/// <summary>An isolated, single-flight plugin binding.</summary>
public interface IPluginSession : IAsyncDisposable
{
    /// <summary>Gets the immutable tenant identity assigned by the trusted host at binding time.</summary>
    string Tenant { get; }

    /// <summary>Gets the current execution identifier, empty before first invocation.</summary>
    string Instance { get; }

    /// <summary>Invokes a contract operation; excess concurrent work fails fast.</summary>
    Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default);
    /// <summary>Stops this instance; subsequent invocations start a fresh instance.</summary>
    Task RestartAsync(CancellationToken cancellationToken = default);
}
