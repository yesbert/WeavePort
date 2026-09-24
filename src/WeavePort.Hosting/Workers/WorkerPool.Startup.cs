namespace WeavePort.Hosting;

internal sealed partial class WorkerPool
{
    internal Task<Worker> StartSharedAsync(ExecutionProfile profile, string version, CancellationToken token) => StartAsync(profile, version, false, token);
    private async Task<Worker> StartAsync(ExecutionProfile profile, string version, bool pristine, CancellationToken token, string? tenant = null, bool freshOnly = false)
    {
        await EnterStartAsync(token);
        Worker? worker = null;
        CancellationTokenSource? deadline = null;
        try
        {
            lock (_sync)
            {
                // Recheck admission and ready workers after waiting for a launch slot.
                Worker? ready = TakeReadyForStartup(profile, version, tenant, freshOnly);
                if (ready is not null)
                {
                    return ready;
                }

                worker = ReserveStartup(profile, version, pristine, tenant);
                deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            }

            deadline.CancelAfter(profile.StartupTimeout);
            await worker.StartAsync(deadline.Token);
            MarkReady(worker, pristine);
            return worker;
        }
        catch (Exception error)
        {
            if (error is not OperationCanceledException and not WorkerCapacityException)
            {
                RuntimeLog.StartupFailed(logger, worker?.Instance ?? "", error.GetType().Name);
            }

            bool timedOut = error is OperationCanceledException && deadline?.IsCancellationRequested == true && !token.IsCancellationRequested && !_lifetime.IsCancellationRequested;
            await CleanupFailedStartupAsync(worker, error, timedOut);
            if (timedOut)
            {
                throw new WorkerStartupException(error, null, true);
            }

            throw;
        }
        finally
        {
            deadline?.Dispose();
            lock (_sync)
            {
                if (worker is not null && --_pendingStarts == 0)
                {
                    _startsDrained!.TrySetResult();
                }
            }

            worker?.StartupCompletion.TrySetResult();
            _starts.Release();
        }
    }

    private async Task CleanupFailedStartupAsync(Worker? worker, Exception primary, bool timedOut)
    {
        if (worker is null)
        {
            return;
        }

        try
        {
            await DestroyAsync(worker);
        }
        catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
        {
            throw new WorkerStartupException(primary, cleanup, timedOut);
        }
    }

    private void MarkReady(Worker worker, bool pristine)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new OperationCanceledException("Pool shutdown interrupted startup.");
            }

            _workersStarted++;
            worker.Starting = false;
            worker.Pristine = pristine;
        }
    }

    private async Task EnterStartAsync(CancellationToken token)
    {
        if (options.WaitForStartCapacity)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _starts.WaitAsync(linked.Token);
        }
        else if (!await _starts.WaitAsync(0, token))
        {
            throw Reject("concurrent-starts");
        }
    }

    private Worker ReserveStartup(ExecutionProfile profile, string version, bool pristine, string? tenant)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pristine && _foregroundAcquires != 0)
        {
            throw Reject("foreground-priority");
        }

        if (_workers.Count >= options.MaximumWorkers || profile.MemoryMiB > options.MemoryBudgetMiB - _memory || (pristine && _workers.Count(w => w.Pristine || w.Starting) >= options.MaximumPristineWorkers))
        {
            throw Reject("pool-reservations");
        }

        if (tenant is not null)
        {
            CheckTenantBudget(tenant, profile.MemoryMiB);
        }

        Worker worker = Normalize(profile).CreateWorker(version, clock);
        worker.Tenant = tenant;
        _workers.Add(worker);
        _memory += profile.MemoryMiB;
        if (_pendingStarts++ == 0)
        {
            _startsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return worker;
    }

    private Worker? TakeReadyForStartup(ExecutionProfile profile, string version, string? tenant, bool freshOnly)
    {
        if (tenant is null)
        {
            return null;
        }

        CheckTenantBudget(tenant, profile.MemoryMiB);
        return TakeReady(profile, version, tenant, freshOnly);
    }
}
