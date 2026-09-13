using Microsoft.Extensions.Logging;
using WeavePort.Internal;
using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed class WorkerCapacityException : IOException;
internal sealed class WorkerPool(WorkerPoolOptions options, TimeProvider clock, ILogger logger) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Worker> _workers = [];
    private readonly Dictionary<(ExecutionProfile Profile, string Version), int> _targets = [];
    private readonly SemaphoreSlim _starts = new(options.MaximumConcurrentStarts);
    private readonly SemaphoreSlim _sweep = new(1);
    private readonly CancellationTokenSource _lifetime = new();
    private long _memory;
    private bool _disposed;
    internal WorkerPoolSnapshot Snapshot(int bindings, int tenants, string? failure)
    {
        lock (_sync)
        {
            return new(_workers.Count, _workers.Count(w => w.Pristine), _workers.Count(w => w.Starting), _workers.Count(w => w.Quarantined), _memory, bindings, tenants, failure)
            {
                OldestQuarantineSeconds = _workers.Where(w => w.Quarantined).Select(w => clock.GetElapsedTime(w.QuarantinedAt!.Value).TotalSeconds).DefaultIfEmpty(0).Max()
            };
        }
    }

    internal async Task<Worker> AcquireAsync(ExecutionProfile profile, string version, string tenant, CancellationToken token)
    {
        List<Worker> reclaim = [];
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CheckTenantBudget(tenant, profile.MemoryMiB);
            Worker? ready = _workers.FirstOrDefault(w => w.Pristine && Matches(w, profile, version) && !Expired(w) && w.Running);
            if (ready is not null)
            {
                ready.Pristine = false;
                ready.Tenant = tenant;
                return ready;
            }

            int reserved = _workers.Count;
            long memory = _memory;
            foreach (Worker candidate in _workers.Where(w => w.Pristine).OrderBy(w => w.ReadyAt))
            {
                if (reserved < options.MaximumWorkers && profile.MemoryMiB <= options.MemoryBudgetMiB - memory)
                {
                    break;
                }

                candidate.Pristine = false;
                candidate.QuarantinedAt ??= clock.GetTimestamp();
                reclaim.Add(candidate);
                reserved--;
                memory -= candidate.Profile.MemoryMiB;
            }
        }

        foreach (Worker candidate in reclaim)
        {
            await DestroyAsync(candidate);
        }

        return await StartAsync(profile, version, false, token, tenant);
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
                    if (previous == 0)
                    {
                        _targets.Remove(key);
                    }
                    else
                    {
                        _targets[key] = previous;
                    }
                }
            }

            throw;
        }
    }

    private async Task<Worker> StartAsync(ExecutionProfile profile, string version, bool pristine, CancellationToken token, string? tenant = null)
    {
        if (!await _starts.WaitAsync(0, token))
        {
            throw Reject("concurrent-starts");
        }

        Worker? worker = null;
        try
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_workers.Count >= options.MaximumWorkers || profile.MemoryMiB > options.MemoryBudgetMiB - _memory || (pristine && _workers.Count(w => w.Pristine || w.Starting) >= options.MaximumPristineWorkers))
                {
                    throw Reject("pool-reservations");
                }

                if (tenant is not null)
                {
                    CheckTenantBudget(tenant, profile.MemoryMiB);
                }

                worker = Normalize(profile).CreateWorker(version, clock);
                worker.Tenant = tenant;
                _workers.Add(worker);
                _memory += profile.MemoryMiB;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await worker.StartAsync(deadline.Token);
            lock (_sync)
            {
                worker.Starting = false;
                worker.Pristine = pristine;
            }

            return worker;
        }
        catch (Exception error)
        {
            if (error is not OperationCanceledException and not WorkerCapacityException)
            {
                RuntimeLog.StartupFailed(logger, worker?.Instance ?? "", error.GetType().Name);
            }

            if (worker is not null)
            {
                await DestroyAsync(worker);
            }

            throw;
        }
        finally
        {
            _starts.Release();
        }
    }

    internal async Task<JsonElement> DestroyAsync(Worker worker)
    {
        lock (_sync)
        {
            worker.Pristine = false;
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
                expired = _workers.Where(w => w.Quarantined || (w.Pristine && ShouldRemove(w))).ToArray();
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

                for (int i = 0; i < needed; i++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        await StartAsync(target.Key.Profile, target.Key.Version, true, token);
                    }
                    catch (WorkerCapacityException)
                    {
                        break;
                    }
                }
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
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        await Cleanup.RunAsync(() => _lifetime.CancelAsync(), ReleaseWorkersAsync);
    }

    private async Task ReleaseWorkersAsync()
    {
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
}
