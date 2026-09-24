using WeavePort.Internal;

namespace WeavePort.Hosting;

internal sealed partial class PluginScheduler
{
    private static readonly TimeSpan AdmissionPollInterval = TimeSpan.FromMilliseconds(100);

    private async Task PumpAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await _signal.WaitAsync(AdmissionPollInterval, _lifetime.Token);
                await DispatchAvailableAsync();
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
                RejectPending(FailureCodes.Busy);
            }
        }
    }

    private async Task DispatchAvailableAsync()
    {
        while (await DispatchNextAsync())
        {
            _lifetime.Token.ThrowIfCancellationRequested();
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
                RejectPending(FailureCodes.Busy);
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

    private void ExpirePending()
    {
        foreach (ScheduledCall call in _queue.ToArray())
        {
            double queued = _clock.GetElapsedTime(call.Enqueued).TotalMilliseconds;
            string? status = PendingStatus(call, queued);
            if (status is null)
            {
                continue;
            }

            _queue.Remove(call);
            _rejected++;
            call.Completion.TrySetResult(ScheduledCall.Rejected(status, queued));
        }
    }

    private string? PendingStatus(ScheduledCall call, double queued)
    {
        if (_closed || call.Plugin.Closed || call.Plugin.Session is SharedInvocationSession shared && shared.Plugin.Snapshot.Disabled)
        {
            return FailureCodes.Disabled;
        }

        if (call.Token.IsCancellationRequested)
        {
            return FailureCodes.Cancelled;
        }

        return _options.QueueTimeout > TimeSpan.Zero && queued >= _options.QueueTimeout.TotalMilliseconds ? FailureCodes.Busy : null;
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
