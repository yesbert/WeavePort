namespace WeavePort.Hosting;

internal sealed partial class WorkerPool
{
    internal async Task PrewarmAsync(ExecutionProfile profile, string version, int count, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var key = (Normalize(profile), version);
        int previous;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (count > options.MaximumPristineWorkers - _targets.Where(p => p.Key != key).Sum(p => p.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Warm targets exceed the shared reserve ceiling.");
            }

            previous = _targets.GetValueOrDefault(key);
            if (count == 0)
            {
                _targets.Remove(key);
            }
            else
            {
                _targets[key] = count;
            }
        }

        try
        {
            await MaintainAsync(token);
        }
        catch
        {
            lock (_sync)
            {
                if (_targets.GetValueOrDefault(key) == count)
                {
                    RestoreWarmTarget(key, previous);
                }
            }

            throw;
        }
    }

    internal async Task MaintainAsync(CancellationToken token)
    {
        await _sweep.WaitAsync(token);
        try
        {
            Worker[] expired;
            KeyValuePair<(ExecutionProfile Profile, string Version), int>[] targets;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                expired = SelectExpiredWorkers();

                foreach (Worker worker in expired)
                {
                    worker.Pristine = false;
                    worker.Reusable = false;
                    worker.QuarantinedAt ??= clock.GetTimestamp();
                }

                targets = _targets.ToArray();
            }

            foreach (Worker worker in expired)
            {
                await DestroyAsync(worker);
            }

            foreach (var target in targets)
            {
                int needed;
                lock (_sync)
                {
                    needed = target.Value - _workers.Count(w => w.Pristine && Matches(w, target.Key.Profile, target.Key.Version));
                }

                await ReplenishAsync(target.Key.Profile, target.Key.Version, needed, token);
            }
        }
        finally
        {
            _sweep.Release();
        }
    }

    // Called under _sync. Count pristine workers in enumeration order before
    // changing any flags: each profile's warm target is a shared retention budget.
    private Worker[] SelectExpiredWorkers()
    {
        var retained = new Dictionary<(ExecutionProfile, string), int>();
        var expired = new List<Worker>();
        foreach (Worker worker in _workers)
        {
            if (!ShouldRetire(worker, retained))
            {
                continue;
            }

            expired.Add(worker);
        }

        return expired.ToArray();
    }

    private bool ShouldRetire(Worker worker, Dictionary<(ExecutionProfile, string), int> retained)
    {
        if (worker.Quarantined)
        {
            return true;
        }

        if (worker.Pristine && ExceedsPristineRetention(worker, retained))
        {
            return true;
        }

        return worker.Reusable && (!worker.Running || clock.GetElapsedTime(worker.ReadyAt) >= options.ReusableIdleTimeout);
    }

    private bool ExceedsPristineRetention(Worker worker, Dictionary<(ExecutionProfile, string), int> retained)
    {
        var key = (worker.Profile, worker.Version);
        int count = retained.GetValueOrDefault(key);
        retained[key] = count + 1;
        return Expired(worker) || !worker.Running || count >= _targets.GetValueOrDefault(key);
    }

    private void RestoreWarmTarget((ExecutionProfile Profile, string Version) key, int previous)
    {
        if (previous == 0)
        {
            _targets.Remove(key);
        }
        else
        {
            _targets[key] = previous;
        }
    }

    private async Task ReplenishAsync(ExecutionProfile profile, string version, int needed, CancellationToken token)
    {
        for (int i = 0; i < needed; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await StartAsync(profile, version, true, token);
            }
            catch (WorkerCapacityException)
            {
                break;
            }
        }
    }
}
