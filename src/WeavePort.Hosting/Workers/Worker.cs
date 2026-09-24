using System.Text.Json;

namespace WeavePort.Hosting;
internal abstract class Worker(ExecutionProfile profile, string version)
{
    internal ExecutionProfile Profile { get; } = profile;
    internal string Version { get; } = version;
    internal virtual string Instance { get; } = "weaveport-" + Guid.NewGuid().ToString("N");
    internal long ReadyAt { get; set; }
    internal string? Tenant { get; set; }
    internal bool Starting { get; set; } = true;
    internal TaskCompletionSource StartupCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Reusable { get; set; }
    internal bool AcquiredFromReuse { get; set; }
    internal bool Pristine { get; set; }
    internal bool Quarantined => QuarantinedAt is not null;
    internal long? QuarantinedAt { get; set; }
    internal McpProtocol? Mcp { get; } = profile is ProcessProfile { Protocol: not ProcessProtocol.Native } local ? new McpProtocol(local.Protocol) : null;
    internal Frames Reader { get; set; } = null!;
    internal abstract Stream Input { get; }
    internal abstract bool Running { get; }

    internal abstract Task StartAsync(CancellationToken token);
    internal abstract Task<JsonElement> DestroyAsync();
}
