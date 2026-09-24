namespace WeavePort.Hosting;
/// <summary>Common policy for the built-in execution adapters. Local reservations are not hard memory limits.</summary>
/// <param name = "MemoryMiB">Memory reservation for admission; enforcement depends on the adapter.</param>
/// <param name = "Timeout">Total invocation deadline.</param>
/// <param name = "IdleTimeout">Optional idle release policy.</param>
public abstract record ExecutionProfile(int MemoryMiB, TimeSpan? Timeout, TimeSpan? IdleTimeout)
{
    internal const int MinimumMemoryMiB = 64;

    /// <summary>Operator-approved reuse policy. Defaults to customer-bound execution; approval covers all code in the compatible deployment.</summary>
    public WorkerReusePolicy ReusePolicy { get; init; }
    /// <summary>Host-authorized normal or heavy scheduling class.</summary>
    public PluginWorkClass WorkClass { get; init; }
    /// <summary>Explicit permission to discard idle state under scheduling pressure.</summary>
    public bool Reconstructible { get; init; }
    /// <summary>Maximum callback operations per invocation.</summary>
    public int MaximumCallbacks { get; init; } = 8;
    /// <summary>Maximum time allowed for worker readiness.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Restrictions requested by this adapter. Effective deployment policy must still be verified.</summary>
    public abstract ExecutionProtections Protection { get; }

    internal abstract Task<ExecutionProfile> ResolveAsync(CancellationToken token);
    internal abstract ExecutionProfile Normalize();
    internal abstract Worker CreateWorker(string version, TimeProvider clock);
}
