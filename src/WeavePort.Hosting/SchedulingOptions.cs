namespace WeavePort.Hosting;
/// <summary>Host-authorized execution class; never select it from untrusted request data.</summary>
public enum PluginWorkClass
{
    /// <summary>Short, reconstructible work.</summary>
    Normal,
    /// <summary>Explicitly admitted long-running work.</summary>
    Heavy
}

/// <summary>Shared scheduler policy. Native memory budgets are reservations, not enforced RSS limits.</summary>
public sealed record SchedulingOptions
{
    /// <summary>Optional explicit worker ceiling for controlled experiments. Null uses memory admission without an independent count limit.</summary>
    public int? MaximumWorkers { get; init; }
    /// <summary>Total configured worker memory reservation.</summary>
    public long MemoryBudgetMiB { get; init; } = 4096;
    /// <summary>Limits simultaneous process launches, not resident worker count. Excess launches wait within the invocation deadline.</summary>
    public int MaximumConcurrentStarts { get; init; } = 8;
    /// <summary>Maximum concurrent heavy calls; must leave at least one worker for normal work.</summary>
    public int MaximumHeavyCalls { get; init; } = 2;
    /// <summary>Maximum concurrent heavy calls belonging to one tenant.</summary>
    public int MaximumHeavyCallsPerTenant { get; init; } = 1;
    /// <summary>Maximum declared heavy plugins per tenant.</summary>
    public int MaximumHeavyPluginsPerTenant { get; init; } = 2;
    /// <summary>Maximum queued calls across all tenants.</summary>
    public int MaximumQueuedCalls { get; init; } = 4096;
    /// <summary>Per-tenant queue bound; this is not a worker reservation.</summary>
    public int MaximumQueuedCallsPerTenant { get; init; } = 128;
    /// <summary>Maximum registered customer-plugin pairs.</summary>
    public int MaximumRegistrations { get; init; } = 10000;
    /// <summary>Maximum serialized payload bytes retained per queued call.</summary>
    public int MaximumPayloadBytes { get; init; } = 1048576;
    /// <summary>Deadline for admission, separate from execution.</summary>
    public TimeSpan QueueTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Normal invocation deadline, including worker startup, transport and callbacks.</summary>
    public TimeSpan NormalTimeout { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Heavy invocation deadline, including startup, transport and callbacks.</summary>
    public TimeSpan HeavyTimeout { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Maximum idle residency; pressure may evict an idle worker sooner.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);
    /// <summary>Maximum idle retention of cleaned approved workers in the shared pool.</summary>
    public TimeSpan ReusableIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Global customer-unassigned reserve, shared by recently requested registered profiles.</summary>
    public int MaximumPristineWorkers { get; init; } = 2;
    /// <summary>Demand expires after this interval; unused profiles receive no reserve.</summary>
    public TimeSpan DemandWindow { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaximumWorkers < 1 || MemoryBudgetMiB < 64 || MaximumConcurrentStarts < 1 || MaximumHeavyCalls < 0 || MaximumHeavyCalls >= MaximumWorkers || MaximumHeavyCallsPerTenant < 1 || MaximumHeavyPluginsPerTenant < 1 || MaximumQueuedCalls < 1 || MaximumQueuedCallsPerTenant < 1 || MaximumRegistrations < 1 || MaximumPayloadBytes < 1 || MaximumPristineWorkers < 0 || MaximumPristineWorkers > MaximumWorkers)
        {
            throw new ArgumentOutOfRangeException(nameof(SchedulingOptions));
        }

        foreach (TimeSpan duration in new[]
        {
            QueueTimeout,
            NormalTimeout,
            HeavyTimeout,
            IdleTimeout,
            ReusableIdleTimeout,
            DemandWindow
        }

        )
        {
            if (duration <= TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(SchedulingOptions));
            }
        }
    }
}

/// <summary>Scheduler counters; runtime reservations include pristine and uncertain cleanup.</summary>
public sealed record SchedulingSnapshot(int Registrations, int Queued, int Active, int HeavyActive, long Completed, long Rejected, long Evictions, long ColdCalls, string? Failure, WorkerPoolSnapshot Runtime);
/// <summary>Complete invocation outcome. Execution includes startup; queue expiry never dispatches.</summary>
public sealed record ScheduledInvocationResult(WeavePort.Abstractions.InvocationResult Result, double QueueMs, double ExecutionMs, bool Cold);
