namespace WeavePort.Hosting;

internal sealed partial class WorkerPool
{
    private long _reuseHits;
    private long _sessionReturns;
    private long _cleanupFailures;
    private long _workersStarted;
    internal async Task ReturnAsync(Worker worker)
    {
        lock (_sync)
        {
            if (!_disposed && !worker.Quarantined && worker.Running && worker.Profile.ReusePolicy == WorkerReusePolicy.ApprovedSessions)
            {
                worker.Tenant = null;
                worker.ReadyAt = clock.GetTimestamp();
                worker.Reusable = true;
                _sessionReturns++;
                return;
            }
        }

        await DestroyAsync(worker);
    }

    internal void RecordCleanupFailure()
    {
        lock (_sync)
        {
            _cleanupFailures++;
        }
    }
}
