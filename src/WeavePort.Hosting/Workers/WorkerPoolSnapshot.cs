namespace WeavePort.Hosting;
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
    /// <summary>Shared worker reservations, including startup but excluding quarantine.</summary>
    public int SharedWorkers { get; init; }
    /// <summary>Configured shared worker memory reservations, excluding quarantine.</summary>
    public long SharedMemoryMiB { get; init; }
    /// <summary>Memory reserved for uncertain removal across all ownership modes.</summary>
    public long QuarantinedMemoryMiB { get; init; }
    /// <summary>Cleaned approved workers currently available for compatible sessions.</summary>
    public int ReusableWorkers { get; init; }
    /// <summary>Assignments from the cleaned approved pool.</summary>
    public long ReuseHits { get; init; }
    /// <summary>Successful returns after cleanup acknowledgement.</summary>
    public long SessionReturns { get; init; }
    /// <summary>Explicit SDK cleanup failures or invalid cleanup acknowledgements.</summary>
    public long SessionCleanupFailures { get; init; }
    /// <summary>Successfully started execution environments.</summary>
    public long WorkersStarted { get; init; }
    /// <summary>Age in seconds since first quarantine entry for the oldest pending removal, including retries; zero when none remain.</summary>
    public double OldestQuarantineSeconds { get; init; }
}
