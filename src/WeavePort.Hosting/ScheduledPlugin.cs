using System.Text.Json;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
/// <summary>Immutable customer-plugin registration. Queues concurrent calls onto one exclusive worker.</summary>
public sealed class ScheduledPlugin : IPluginOperationSession
{
    private readonly PluginScheduler _owner;
    internal IPluginSession Session { get; }
    internal ExecutionProfile Profile { get; }
    internal string Plugin { get; }
    internal string Version { get; }
    internal PluginWorkClass WorkClass { get; }
    internal bool Active { get; set; }
    internal bool Resident { get; set; }
    internal bool Closed { get; set; }
    internal long LastUsed { get; set; }
    internal long? LastDemand { get; set; }
    internal TaskCompletionSource Idle { get; set; } = NewSignal();
    internal Task? Disposal { get; set; }

    internal static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ScheduledPlugin(PluginScheduler owner, IPluginSession session, ExecutionProfile profile, PluginContext context, PluginWorkClass workClass)
    {
        _owner = owner;
        Session = session;
        Profile = profile;
        Plugin = context.Plugin;
        Version = context.Version;
        WorkClass = workClass;
        Idle.TrySetResult();
    }

    /// <inheritdoc/>
    public bool SupportsStreaming => true;

    /// <inheritdoc/>
    public ValueTask<IPluginSession> AcquireOperationAsync(CancellationToken cancellationToken = default) => _owner.AcquireOperationAsync(this, cancellationToken);
    /// <inheritdoc/>
    public string Tenant => Session.Tenant;
    /// <inheritdoc/>
    public string Instance => Session.Instance;

    /// <summary>Queues a complete operation. Payload is frozen; elapsed time includes admission waiting.</summary>
    public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default) => (await InvokeMeasuredAsync(operation, payload, cancellationToken)).Result;
    /// <summary>Returns admission and execution timing separately for capacity measurement.</summary>
    public Task<ScheduledInvocationResult> InvokeMeasuredAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default) => _owner.Enqueue(this, operation, payload, cancellationToken);
    /// <summary>Releases the current idle worker. Throws if a call or cleanup is active.</summary>
    public Task RestartAsync(CancellationToken cancellationToken = default) => _owner.RestartAsync(this, cancellationToken);
    /// <summary>Closes admission, cancels active work and releases this registration.</summary>
    public ValueTask DisposeAsync() => new(_owner.RemoveAsync(this));
}
