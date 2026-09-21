namespace WeavePort.Hosting;
internal sealed partial class PluginScheduler
{
    private async Task PumpAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await _signal.WaitAsync(TimeSpan.FromMilliseconds(100), _lifetime.Token);
                while (await DispatchNextAsync())
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                }

                await MaintainReserveAsync();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        // Owned shutdown cancels admission and active calls.
        }
        catch (Exception error)
        {
            lock (_sync)
            {
                _failure = error.GetType().Name;
                RejectPending("busy");
            }
        }
    }

    private async Task<bool> DispatchNextAsync()
    {
        ScheduledPlugin? evict;
        lock (_sync)
        {
            ExpirePending();
            if (_host.Snapshot.MaintenanceFailure is { } maintenanceFailure)
            {
                _failure = maintenanceFailure;
                RejectPending("busy");
            }

            if (_closed || _failure is not null)
            {
                return false;
            }

            ScheduledCall? call = SelectCall();
            evict = FindEviction(call);
            if (evict is not null)
            {
                SetActive(evict, true);
                evict.Idle = ScheduledPlugin.NewSignal();
            }
            else if (call is not null && Fits(call.Plugin))
            {
                return Admit(call);
            }
        }

        if (evict is null)
        {
            return false;
        }

        await ReleaseResidentAsync(evict, _lifetime.Token);
        return true;
    }

    private bool Admit(ScheduledCall call)
    {
        if (call.Plugin.Session is SharedInvocationSession shared && !shared.Reserve())
        {
            return false;
        }

        _queue.Remove(call);
        bool cold = !call.Plugin.Resident;
        SetActive(call.Plugin, true);
        SetResident(call.Plugin, true);
        call.Plugin.Idle = ScheduledPlugin.NewSignal();
        _turns[call.Plugin.Tenant] = ++_turn;
        _ = Task.Run(() => RunAsync(call, cold));
        return true;
    }

    private ScheduledCall? SelectCall()
    {
        ScheduledPlugin[] active = _active.ToArray();
        var counts = active.GroupBy(p => p.Tenant).ToDictionary(group => group.Key, group => group.Count());
        return _queue.Where(c => Eligible(c.Plugin, active)).OrderBy(c => Math.Max(counts.GetValueOrDefault(c.Plugin.Tenant), _host.ActiveCallsFor(c.Plugin.Tenant))).ThenBy(c => _turns.GetValueOrDefault(c.Plugin.Tenant)).FirstOrDefault();
    }

    private bool Eligible(ScheduledPlugin plugin, ScheduledPlugin[] active)
    {
        if (plugin.Session is SharedInvocationSession shared && !shared.Available)
        {
            return false;
        }

        if (plugin.Active || plugin.Closed || _host.ActiveCallsFor(plugin.Tenant) >= _options.MaximumCallsPerTenant || active.Count(p => p.Tenant == plugin.Tenant) >= _options.MaximumCallsPerTenant || plugin.Session is not SharedInvocationSession && (active.Count(p => p.Session is not SharedInvocationSession) >= _workerLimit || active.Where(p => p.Session is not SharedInvocationSession).Sum(p => (long)p.Profile.MemoryMiB) + plugin.Profile.MemoryMiB > _options.MemoryBudgetMiB))
        {
            return false;
        }

        if (plugin.WorkClass == PluginWorkClass.Normal)
        {
            return true;
        }

        ScheduledPlugin[] heavy = active.Where(p => p.WorkClass == PluginWorkClass.Heavy).ToArray();
        return heavy.Length < _options.MaximumHeavyCalls && heavy.Count(p => p.Tenant == plugin.Tenant) < _options.MaximumHeavyCallsPerTenant && HeavyMemory(heavy, plugin) <= _options.MemoryBudgetMiB / 2;
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
        return residents.Length + runtime.SharedWorkers + runtime.Quarantined < _workerLimit && residents.Sum(p => (long)p.Profile.MemoryMiB) + runtime.SharedMemoryMiB + runtime.QuarantinedMemoryMiB + plugin.Profile.MemoryMiB <= _options.MemoryBudgetMiB;
    }

    private ScheduledPlugin? FindEviction(ScheduledCall? call)
    {
        bool pressure = call is not null && !Fits(call.Plugin);
        return _residents.Where(p => p.Profile.Reconstructible && !p.Active && !p.Closed && (_clock.GetElapsedTime(p.LastUsed) >= _options.IdleTimeout || pressure && p != call!.Plugin)).OrderBy(p => p.LastUsed).FirstOrDefault();
    }

    private void ExpirePending()
    {
        foreach (ScheduledCall call in _queue.ToArray())
        {
            double queued = _clock.GetElapsedTime(call.Enqueued).TotalMilliseconds;
            string? status = PendingStatus(call, queued);
            if (status is not null)
            {
                _queue.Remove(call);
                _rejected++;
                call.Completion.TrySetResult(ScheduledCall.Rejected(status, queued));
            }
        }
    }

    private string? PendingStatus(ScheduledCall call, double queued)
    {
        if (_closed || call.Plugin.Closed || call.Plugin.Session is SharedInvocationSession shared && shared.Plugin.Snapshot.Disabled)
        {
            return "disabled";
        }

        if (call.Token.IsCancellationRequested)
        {
            return "cancelled";
        }

        return _options.QueueTimeout > TimeSpan.Zero && queued >= _options.QueueTimeout.TotalMilliseconds ? "busy" : null;
    }

    private async Task RunAsync(ScheduledCall call, bool cold)
    {
        long started = _clock.GetTimestamp();
        double queued = _clock.GetElapsedTime(call.Enqueued, started).TotalMilliseconds;
        ScheduledInvocationResult? completed = null;
        Exception? failure = null;
        try
        {
            using var token = CancellationTokenSource.CreateLinkedTokenSource(call.Token, _lifetime.Token);
            var result = call.IsLease ? await RunLeaseAsync(call, token.Token) : await call.Plugin.Session.InvokeAsync(call.Operation, call.Payload, token.Token);
            double execution = _clock.GetElapsedTime(started).TotalMilliseconds;
            lock (_sync)
            {
                var session = call.Plugin.Session as PluginSession;
                cold &= session is not null && !session.LastAcquisitionReused;
                _coldCalls += cold ? 1 : 0;
                SetResident(call.Plugin, session?.HasWorker == true);
                _completed++;
                completed = new(result with { ElapsedMs = queued + execution }, queued, execution, cold);
            }
        }
        catch (OperationCanceledException) when (call.Token.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
            completed = ScheduledCall.Rejected(_lifetime.IsCancellationRequested ? "disabled" : "cancelled", queued);
        }
        catch (Exception error)
        {
            lock (_sync)
            {
                _failure = error.GetType().Name;
                failure = error;
                RejectPending("busy");
            }
        }
        finally
        {
            lock (_sync)
            {
                call.Plugin.LastUsed = _clock.GetTimestamp();
                SetActive(call.Plugin, false);
                if (call.Plugin.Session is SharedInvocationSession && !_tenantRegistrations.ContainsKey(call.Plugin.Tenant) && !_active.Any(p => p.Tenant == call.Plugin.Tenant) && !_queue.Any(c => c.Plugin.Tenant == call.Plugin.Tenant))
                {
                    _turns.Remove(call.Plugin.Tenant);
                }

                call.Plugin.Idle.TrySetResult();
                Wake();
                if (failure is not null)
                {
                    call.Completion.TrySetException(failure);
                }
                else
                {
                    call.Completion.TrySetResult(completed!);
                }
            }
        }
    }

    private void RejectPending(string status)
    {
        foreach (ScheduledCall call in _queue)
        {
            _rejected++;
            call.Completion.TrySetResult(ScheduledCall.Rejected(status, _clock.GetElapsedTime(call.Enqueued).TotalMilliseconds));
        }

        _queue.Clear();
    }
}
