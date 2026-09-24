namespace WeavePort.Hosting;

internal sealed partial class WorkerPool
{
    internal async Task<Worker> AcquireAsync(ExecutionProfile profile, string version, string tenant, CancellationToken token, bool freshOnly = false)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _foregroundAcquires++;
        }

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var (ready, pending, reclaim) = PrepareAcquisition(profile, version, tenant, freshOnly);
                if (ready is not null)
                {
                    return ready;
                }

                if (pending is not null)
                {
                    await pending.WaitAsync(token);
                    continue;
                }

                await DestroyReclaimedAsync(reclaim);
                try
                {
                    return await StartAsync(profile, version, false, token, tenant, freshOnly);
                }
                catch (WorkerCapacityException) when (options.WaitForStartCapacity && CanRetryAcquisition(profile))
                {
                    // A startup already in flight can change capacity before reservation. Recheck under the lock.
                }
            }
        }
        finally
        {
            lock (_sync)
            {
                _foregroundAcquires--;
            }
        }
    }

    private (Worker? Ready, Task? Pending, List<Worker> Reclaim) PrepareAcquisition(ExecutionProfile profile, string version, string tenant, bool freshOnly)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CheckTenantBudget(tenant, profile.MemoryMiB);
            Worker? ready = TakeReady(profile, version, tenant, freshOnly);
            if (ready is not null)
            {
                return (ready, null, []);
            }

            if (TryGetPendingWarmup(profile, out Task warming))
            {
                return (null, warming, []);
            }

            return (null, null, ReclaimPristine(profile));
        }
    }

    private Worker? TakeReady(ExecutionProfile profile, string version, string tenant, bool freshOnly = false)
    {
        Worker? ready = _workers.FirstOrDefault(candidate =>
            (candidate.Pristine && !Expired(candidate) ||
             !freshOnly && candidate.Reusable && clock.GetElapsedTime(candidate.ReadyAt) < options.ReusableIdleTimeout) &&
            Matches(candidate, profile, version) && candidate.Running);
        if (ready is null)
        {
            return null;
        }

        ready.AcquiredFromReuse = ready.Reusable;
        _reuseHits += ready.Reusable ? 1 : 0;
        ready.Reusable = false;
        ready.Pristine = false;
        ready.Tenant = tenant;

        return ready;
    }

    private List<Worker> ReclaimPristine(ExecutionProfile profile)
    {
        List<Worker> reclaim = [];
        int reserved = _workers.Count;
        long memory = _memory;
        foreach (Worker candidate in _workers.Where(w => w.Pristine || w.Reusable).OrderBy(w => w.ReadyAt))
        {
            if (reserved < options.MaximumWorkers && profile.MemoryMiB <= options.MemoryBudgetMiB - memory)
            {
                break;
            }

            candidate.Pristine = false;
            candidate.Reusable = false;
            candidate.QuarantinedAt ??= clock.GetTimestamp();
            reclaim.Add(candidate);
            reserved--;
            memory -= candidate.Profile.MemoryMiB;
        }

        return reclaim;
    }

    private bool HasCapacity(ExecutionProfile profile) => _workers.Count < options.MaximumWorkers && profile.MemoryMiB <= options.MemoryBudgetMiB - _memory;
    private bool CanRetryAcquisition(ExecutionProfile profile)
    {
        lock (_sync)
        {
            return !_disposed && (HasCapacity(profile) || _workers.Any(w => w.Pristine || w.Reusable || w.Starting && w.Tenant is null));
        }
    }

    private bool TryGetPendingWarmup(ExecutionProfile profile, out Task pending)
    {
        pending = Task.CompletedTask;
        if (!options.WaitForStartCapacity || HasCapacity(profile))
        {
            return false;
        }

        Task[] warming = _workers.Where(w => w.Starting && w.Tenant is null).Select(w => w.StartupCompletion.Task).ToArray();
        // Resume as soon as any shared startup changes capacity.
        if (warming.Length == 0)
        {
            return false;
        }

        pending = Task.WhenAny(warming);
        return true;
    }

    private async Task DestroyReclaimedAsync(IEnumerable<Worker> workers)
    {
        foreach (Worker worker in workers)
        {
            await DestroyAsync(worker);
        }
    }
}
