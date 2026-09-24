using WeavePort.Internal;

namespace WeavePort.Hosting;

internal sealed partial class PluginScheduler
{
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
                completed = new(result with
                {
                    ElapsedMs = queued + execution
                }, queued, execution, cold);
            }
        }
        catch (OperationCanceledException) when (call.Token.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
            completed = ScheduledCall.Rejected(_lifetime.IsCancellationRequested ? FailureCodes.Disabled : FailureCodes.Cancelled, queued);
        }
        catch (Exception error)
        {
            lock (_sync)
            {
                _failure = error.GetType().Name;
                failure = error;
                RejectPending(FailureCodes.Busy);
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
}
