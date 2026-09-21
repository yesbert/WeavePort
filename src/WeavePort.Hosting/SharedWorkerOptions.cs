using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;
/// <summary>Bounds resident shared execution; configured memory must cover this degree.</summary>
public sealed record SharedWorkerOptions
{
    /// <summary>Maximum concurrent invocations in each worker.</summary>
    public int Degree { get; init; } = 16;
    /// <summary>Resident process count; select using deployment measurements.</summary>
    public int Workers { get; init; } = 1;
    /// <summary>Maximum replacements within RestartWindow.</summary>
    public int MaximumRestarts { get; init; } = 3;
    /// <summary>Sliding crash accounting window.</summary>
    public TimeSpan RestartWindow { get; init; } = TimeSpan.FromMinutes(1);
    /// <summary>Maximum abandoned slots before retiring the worker.</summary>
    public int MaximumAbandonedCalls { get; init; } = 4;
    /// <summary>Maximum time an abandoned invocation may retain a slot.</summary>
    public TimeSpan CancellationGrace { get; init; } = TimeSpan.FromSeconds(5);
    /// <summary>Maximum channel silence while work remains outstanding.</summary>
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromMinutes(5);

    internal void Validate() => Validate(this);
    private static void Validate(SharedWorkerOptions options)
    {
        if (options.Degree is < 1 or > 1024 || options.Workers is < 1 or > 1024 || options.MaximumRestarts < 0 || options.MaximumAbandonedCalls < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Shared worker counts are outside their supported ranges.");
        }

        foreach (TimeSpan value in new[]
        {
            options.RestartWindow,
            options.CancellationGrace,
            options.SilenceTimeout
        }

        )
        {
            if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Shared worker timeouts must be positive and fit the timer range.");
            }
        }
    }
}

/// <summary>Resident shared plugin. Tenant views own no worker and cannot restart it.</summary>
public interface ISharedPlugin : IAsyncDisposable
{
    /// <summary>Returns a cheap tenant-bound client using immutable host authority.</summary>
    IBoundPluginClient For(string tenant);
    /// <summary>Observes current residency and restart state.</summary>
    SharedPluginSnapshot Snapshot { get; }
}

/// <summary>Current shared process and slot state, including cancelled work still executing.</summary>
public sealed record SharedPluginSnapshot(int ReadyWorkers, int ActiveCalls, int AbandonedCalls, int Restarts, bool Disabled)
{
    /// <summary>Last lifecycle failure type when cleanup prevents recovery; excludes plugin text.</summary>
    public string? Failure { get; init; }
}
