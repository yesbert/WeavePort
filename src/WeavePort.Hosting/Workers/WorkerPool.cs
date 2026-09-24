using Microsoft.Extensions.Logging;
using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed partial class WorkerPool(WorkerPoolOptions options, TimeProvider clock, ILogger logger) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Worker> _workers = [];
    private readonly Dictionary<(ExecutionProfile Profile, string Version), int> _targets = [];
    private readonly SemaphoreSlim _starts = new(options.MaximumConcurrentStarts);
    private readonly SemaphoreSlim _sweep = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _disposal;
    private int _pendingStarts;
    private int _foregroundAcquires;
    private TaskCompletionSource? _startsDrained;
    private long _memory;
    private bool _disposed;
    internal WorkerPoolSnapshot Snapshot(int bindings, int tenants, string? failure)
    {
        lock (_sync)
        {
            return new(_workers.Count, _workers.Count(w => w.Pristine), _workers.Count(w => w.Starting), _workers.Count(w => w.Quarantined), _memory, bindings, tenants, failure)
            {
                SharedWorkers = _workers.Count(w => w.Profile.ReusePolicy == WorkerReusePolicy.Shared && !w.Quarantined),
                SharedMemoryMiB = _workers.Where(w => w.Profile.ReusePolicy == WorkerReusePolicy.Shared && !w.Quarantined).Sum(w => (long)w.Profile.MemoryMiB),
                QuarantinedMemoryMiB = _workers.Where(w => w.Quarantined).Sum(w => (long)w.Profile.MemoryMiB),
                ReusableWorkers = _workers.Count(w => w.Reusable),
                ReuseHits = _reuseHits,
                SessionReturns = _sessionReturns,
                SessionCleanupFailures = _cleanupFailures,
                WorkersStarted = _workersStarted,
                OldestQuarantineSeconds = _workers.Where(w => w.Quarantined).Select(w => clock.GetElapsedTime(w.QuarantinedAt!.Value).TotalSeconds).DefaultIfEmpty(0).Max()
            };
        }
    }

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

    internal async Task<JsonElement> DestroyAsync(Worker worker)
    {
        lock (_sync)
        {
            worker.Pristine = false;
            worker.Reusable = false;
            worker.QuarantinedAt ??= clock.GetTimestamp();
            worker.Starting = false;
        }

        JsonElement termination;
        try
        {
            termination = await worker.DestroyAsync();
        }
        catch (Exception error)
        {
            RuntimeLog.CleanupFailed(logger, worker.Instance, error.GetType().Name);
            throw;
        }

        lock (_sync)
        {
            if (_workers.Remove(worker))
            {
                _memory -= worker.Profile.MemoryMiB;
            }
        }

        return termination;
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

                var retained = new Dictionary<(ExecutionProfile, string), int>();
                expired = _workers.Where(w => w.Quarantined || (w.Pristine && ShouldRemove(w)) || (w.Reusable && (!w.Running || clock.GetElapsedTime(w.ReadyAt) >= options.ReusableIdleTimeout))).ToArray();
                bool ShouldRemove(Worker worker)
                {
                    var key = (worker.Profile, worker.Version);
                    retained.TryGetValue(key, out int count);
                    retained[key] = count + 1;
                    return Expired(worker) || !worker.Running || count >= _targets.GetValueOrDefault(key);
                }

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

    private void CheckTenantBudget(string tenant, int memoryMiB)
    {
        Worker[] assigned = _workers.Where(w => w.Tenant == tenant).ToArray();
        if (assigned.Length >= options.MaximumWorkersPerTenant || memoryMiB > options.MemoryBudgetPerTenantMiB - assigned.Sum(w => (long)w.Profile.MemoryMiB))
        {
            throw Reject("tenant-reservations");
        }
    }

    private WorkerCapacityException Reject(string reason)
    {
        RuntimeLog.AdmissionRejected(logger, reason);
        return new WorkerCapacityException();
    }

    private bool Expired(Worker worker) => clock.GetElapsedTime(worker.ReadyAt) >= (options.PristineLifetime ?? TimeSpan.FromSeconds(30));
    private static ExecutionProfile Normalize(ExecutionProfile profile) => profile.Normalize();
    private static bool Matches(Worker worker, ExecutionProfile profile, string version) => worker.Profile == Normalize(profile) && worker.Version == version;
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            return new ValueTask(_disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await Cleanup.RunAsync(() => _lifetime.CancelAsync(), ReleaseWorkersAsync);
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ReleaseWorkersAsync()
    {
        Task starts;
        lock (_sync)
        {
            starts = _startsDrained?.Task ?? Task.CompletedTask;
        }

        await starts;
        await _sweep.WaitAsync();
        try
        {
            Worker[] remaining;
            lock (_sync)
            {
                _targets.Clear();
                remaining = _workers.ToArray();
            }

            List<Exception> failures = [];
            foreach (Worker worker in remaining)
            {
                try
                {
                    await DestroyAsync(worker);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }

            if (failures.Count != 0)
            {
                throw new AggregateException("Worker cleanup could not be confirmed.", failures);
            }
        }
        finally
        {
            _sweep.Release();
        }
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
