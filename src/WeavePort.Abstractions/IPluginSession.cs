using System.Text.Json;

namespace WeavePort.Abstractions;
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
