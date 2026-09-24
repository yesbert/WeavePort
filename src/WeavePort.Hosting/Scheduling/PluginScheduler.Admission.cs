using WeavePort.Internal;

namespace WeavePort.Hosting;

internal sealed partial class PluginScheduler
{
    private ScheduledCall? SelectCall()
    {
        ScheduledPlugin[] active = _active.ToArray();
        var counts = active.GroupBy(p => p.Tenant).ToDictionary(group => group.Key, group => group.Count());
        return _queue
            .Where(call => Eligible(call.Plugin, active))
            .OrderBy(call => Math.Max(counts.GetValueOrDefault(call.Plugin.Tenant), _host.ActiveCallsFor(call.Plugin.Tenant)))
            .ThenBy(call => _turns.GetValueOrDefault(call.Plugin.Tenant))
            .FirstOrDefault();
    }

    private bool Eligible(ScheduledPlugin plugin, ScheduledPlugin[] active)
    {
        if (plugin.Session is SharedInvocationSession shared && !shared.Available)
        {
            return false;
        }

        if (plugin.Active || plugin.Closed ||
            _host.ActiveCallsFor(plugin.Tenant) >= _options.MaximumCallsPerTenant ||
            active.Count(candidate => candidate.Tenant == plugin.Tenant) >= _options.MaximumCallsPerTenant)
        {
            return false;
        }

        if (plugin.Session is not SharedInvocationSession && !FitsExclusiveBudget(plugin, active))
        {
            return false;
        }

        if (plugin.WorkClass == PluginWorkClass.Normal)
        {
            return true;
        }

        ScheduledPlugin[] heavy = active.Where(p => p.WorkClass == PluginWorkClass.Heavy).ToArray();
        return heavy.Length < _options.MaximumHeavyCalls &&
            heavy.Count(candidate => candidate.Tenant == plugin.Tenant) < _options.MaximumHeavyCallsPerTenant &&
            HeavyMemory(heavy, plugin) <= _options.MemoryBudgetMiB / 2;
    }

    private bool FitsExclusiveBudget(ScheduledPlugin plugin, ScheduledPlugin[] active)
    {
        IEnumerable<ScheduledPlugin> exclusive = active.Where(candidate => candidate.Session is not SharedInvocationSession);
        return exclusive.Count() < _workerLimit &&
            exclusive.Sum(candidate => (long)candidate.Profile.MemoryMiB) + plugin.Profile.MemoryMiB <= _options.MemoryBudgetMiB;
    }

    private static long HeavyMemory(ScheduledPlugin[] active, ScheduledPlugin next)
    {
        var plugins = active.Append(next).ToArray();
        long exclusive = plugins.Where(p => p.Session is not SharedInvocationSession).Sum(p => (long)p.Profile.MemoryMiB);
        long shared = plugins.Select(p => p.Session).OfType<SharedInvocationSession>().Select(s => s.Plugin).Distinct().Sum(p => (long)p.Profile.MemoryMiB * p.Options.Workers);
        return exclusive + shared;
    }

    private bool Fits(ScheduledPlugin plugin)
    {
        if (plugin.Session is SharedInvocationSession || plugin.Resident)
        {
            return true;
        }

        ScheduledPlugin[] residents = _residents.ToArray();
        WorkerPoolSnapshot runtime = _host.Snapshot;
        long residentMemoryMiB = residents.Sum(resident => (long)resident.Profile.MemoryMiB);
        long projectedMemoryMiB = residentMemoryMiB + runtime.SharedMemoryMiB +
            runtime.QuarantinedMemoryMiB + plugin.Profile.MemoryMiB;
        return residents.Length + runtime.SharedWorkers + runtime.Quarantined < _workerLimit &&
            projectedMemoryMiB <= _options.MemoryBudgetMiB;
    }

    private ScheduledPlugin? FindEviction(ScheduledCall? call)
    {
        bool pressure = call is not null && !Fits(call.Plugin);
        return _residents
            .Where(plugin => plugin.Profile.Reconstructible && !plugin.Active && !plugin.Closed)
            .Where(plugin => _clock.GetElapsedTime(plugin.LastUsed) >= _options.IdleTimeout ||
                pressure && plugin != call!.Plugin)
            .OrderBy(plugin => plugin.LastUsed)
            .FirstOrDefault();
    }
}
