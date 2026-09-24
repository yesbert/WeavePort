namespace WeavePort.Hosting;
/// <summary>Limits shared by all tenants using one host coordinator. Memory counts adapter reservations, not RSS; native reservations are not enforced ceilings.</summary>
/// <param name = "MaximumWorkers">Maximum starting, pristine, assigned or cleanup-uncertain workers.</param>
/// <param name = "MemoryBudgetMiB">Sum of worker memory reservations.</param>
/// <param name = "MaximumPristineWorkers">Maximum shared unassigned reserve across all images.</param>
/// <param name = "MaximumConcurrentStarts">Maximum simultaneous adapter startups.</param>
/// <param name = "PristineLifetime">Maximum time to retain an unused ready worker.</param>
/// <param name = "MaintenanceInterval">Interval for replenishment, expiry and opted-in idle release.</param>
/// <param name = "MaximumWorkersPerTenant">Maximum assigned workers for any one tenant, including quarantine.</param>
/// <param name = "MemoryBudgetPerTenantMiB">Maximum summed assigned worker memory reservations per tenant.</param>
public sealed record WorkerPoolOptions(int MaximumWorkers = 64, long MemoryBudgetMiB = 16384, int MaximumPristineWorkers = 4, int MaximumConcurrentStarts = 8, TimeSpan? PristineLifetime = null, TimeSpan? MaintenanceInterval = null, int MaximumWorkersPerTenant = 8, long MemoryBudgetPerTenantMiB = 2048)
{
    /// <summary>Optional protected destination for local exception details. Called synchronously; keep delivery bounded. Destination failures cannot replace operation outcomes.</summary>
    public Action<FailureDiagnostic>? DetailedFailureSink { get; init; }
    /// <summary>Wait for launch slots and pending pristine capacity within the caller deadline; default direct-host admission remains fail-fast.</summary>
    public bool WaitForStartCapacity { get; init; }
    /// <summary>Maximum idle retention of successfully cleaned approved workers, independent of pristine reserve targets.</summary>
    public TimeSpan ReusableIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
