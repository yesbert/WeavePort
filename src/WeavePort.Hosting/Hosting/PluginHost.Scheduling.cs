using WeavePort.Abstractions;
using Microsoft.Extensions.Logging;

namespace WeavePort.Hosting;

public sealed partial class PluginHost
{
    private readonly PluginScheduler? _scheduler;
    /// <summary>Creates one host with bounded fair admission and a single shared worker budget.</summary>
    public PluginHost(SchedulingOptions options, TimeProvider? timeProvider = null) : this(options.MaximumCallsPerTenant, PoolOptions(options), timeProvider)
    {
        _scheduler = new PluginScheduler(this, options, _clock);
    }

    /// <summary>Creates a queued host with caller-owned logging and one worker budget.</summary>
    public PluginHost(SchedulingOptions options, ILogger<PluginHost> logger, TimeProvider? timeProvider = null) : this(logger, options.MaximumCallsPerTenant, PoolOptions(options), timeProvider)
    {
        _scheduler = new PluginScheduler(this, options, _clock);
    }

    /// <summary>Observes queued admission and execution separately from worker reservations.</summary>
    public SchedulingSnapshot? Scheduling => _scheduler?.Snapshot;

    private static WorkerPoolOptions PoolOptions(SchedulingOptions options)
    {
        SchedulingOptions.Validate(options);
        int workers = options.MaximumWorkers ?? (int)Math.Min(int.MaxValue, options.MemoryBudgetMiB / ExecutionProfile.MinimumMemoryMiB);
        return new WorkerPoolOptions(workers, options.MemoryBudgetMiB, Math.Min(workers, options.MaximumPristineWorkers), options.MaximumConcurrentStarts, MaximumWorkersPerTenant: workers, MemoryBudgetPerTenantMiB: options.MemoryBudgetMiB)
        {
            WaitForStartCapacity = true,
            ReusableIdleTimeout = options.ReusableIdleTimeout
        };
    }

    /// <summary>Binds a tenant using this host's admission policy. Zero queue wait means fail-fast.</summary>
    public async Task<IPluginSession> BindAsync(PluginContext context, ExecutionProfile profile, IHostCallbacks callbacks, IEnumerable<string> grants, ExecutionProtections requiredProtection = ExecutionProtections.None, CancellationToken cancellationToken = default)
    {
        if (_scheduler is null)
        {
            return await BindDirectAsync(context, profile, callbacks, grants, requiredProtection, cancellationToken);
        }

        return await _scheduler.RegisterAsync(context, profile, callbacks, grants, profile.WorkClass, requiredProtection, cancellationToken);
    }
}
