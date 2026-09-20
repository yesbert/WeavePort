namespace WeavePort.Hosting;
internal sealed partial class WorkerPool
{
    private async Task<Worker> StartAsync(ExecutionProfile profile, string version, bool pristine, CancellationToken token, string? tenant = null)
    {
        await EnterStartAsync(token);
        Worker? worker = null;
        CancellationTokenSource? deadline = null;
        try
        {
            lock (_sync)
            {
                worker = ReserveStartup(profile, version, pristine, tenant);
                deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            }

            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await worker.StartAsync(deadline.Token);
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new OperationCanceledException("Pool shutdown interrupted startup.");
                }

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
            deadline?.Dispose();
            lock (_sync)
            {
                if (worker is not null && --_pendingStarts == 0)
                {
                    _startsDrained!.TrySetResult();
                }
            }

            _starts.Release();
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
}
