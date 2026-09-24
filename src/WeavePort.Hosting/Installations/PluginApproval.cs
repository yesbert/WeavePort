using System.Text.Json;
using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;
/// <summary>One operator decision authorizing trusted local code and bounding the manifest's launch requirements.</summary>
public sealed record PluginApproval
{
    /// <summary>Explicit acknowledgement that the plugin and its dependencies are trusted local code.</summary>
    public bool TrustedCode { get; init; }
    /// <summary>Approved ownership mode. Shared requires ShareAsync.</summary>
    public WorkerReusePolicy Ownership { get; init; } = WorkerReusePolicy.CustomerBound;
    /// <summary>Upper bound on the installation's requested reservation per worker.</summary>
    public int MaximumMemoryMiB { get; init; } = 256;
    /// <summary>Approved simultaneous calls per shared worker, capped by the declaration.</summary>
    public int Degree { get; init; } = 1;
    /// <summary>Number of resident shared processes.</summary>
    public int Workers { get; init; } = 1;
    /// <summary>Unary invocation deadline.</summary>
    public TimeSpan InvocationTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Worker readiness deadline.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Operator-authorized normal or heavy admission class.</summary>
    public PluginWorkClass WorkClass { get; init; } = PluginWorkClass.Normal;
    /// <summary>Callback operations allowed during each invocation.</summary>
    public int MaximumCallbacks { get; init; } = 8;
    /// <summary>Explicit permission to evict idle, reconstructible exclusive workers.</summary>
    public bool Reconstructible { get; init; }
    /// <summary>Independent stream exchange and whole-operation deadlines.</summary>
    public PluginStreamOptions Streams { get; init; } = new();
    /// <summary>Opt-in raw stderr diagnostics, which may contain tenant data. Delivery is bounded and best-effort.</summary>
    public bool ForwardStandardError { get; init; }
    /// <summary>Maximum shared worker replacements within RestartWindow.</summary>
    public int MaximumRestarts { get; init; } = 3;
    /// <summary>Sliding shared failure accounting window.</summary>
    public TimeSpan RestartWindow { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Maximum cancelled but still-running calls before worker retirement.</summary>
    public int MaximumAbandonedCalls { get; init; } = 4;
    /// <summary>Maximum retained lifetime of a cancelled shared invocation.</summary>
    public TimeSpan CancellationGrace { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Maximum channel silence while shared work remains outstanding.</summary>
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Optional absolute private workspace parent.</summary>
    public string? WorkspaceRoot { get; init; }
}
