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
/// <param name = "WaitForStartCapacity">When true, wait for a launch slot within the caller deadline; default direct-host admission remains fail-fast.</param>
public sealed record WorkerPoolOptions(int MaximumWorkers = 64, long MemoryBudgetMiB = 16384, int MaximumPristineWorkers = 4, int MaximumConcurrentStarts = 8, TimeSpan? PristineLifetime = null, TimeSpan? MaintenanceInterval = null, int MaximumWorkersPerTenant = 8, long MemoryBudgetPerTenantMiB = 2048, bool WaitForStartCapacity = false);
/// <summary>Point-in-time coordinator accounting. Reserved memory is not measured residency.</summary>
/// <param name = "Workers">All reserved execution environments.</param>
/// <param name = "Pristine">Ready customer-unassigned workers.</param>
/// <param name = "Starting">Workers reserved and still starting.</param>
/// <param name = "Quarantined">Workers whose removal has not been confirmed.</param>
/// <param name = "ReservedMemoryMiB">Sum of configured reservations, including quarantine.</param>
/// <param name = "Bindings">Registered, undisposed bindings.</param>
/// <param name = "Tenants">Tenant admission records, including outstanding callbacks.</param>
/// <param name = "MaintenanceFailure">Last background maintenance failure type, if any.</param>
public sealed record WorkerPoolSnapshot(int Workers, int Pristine, int Starting, int Quarantined, long ReservedMemoryMiB, int Bindings, int Tenants, string? MaintenanceFailure)
{
    /// <summary>Age in seconds since first quarantine entry for the oldest pending removal, including retries; zero when none remain.</summary>
    public double OldestQuarantineSeconds { get; init; }
}
