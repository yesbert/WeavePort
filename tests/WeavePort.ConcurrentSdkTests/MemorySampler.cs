using System.Diagnostics;

internal sealed class MemorySampler : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Process _host = Process.GetCurrentProcess();
    private readonly Process _worker;
    private readonly Task _sampling;
    internal long HostInitialBytes { get; }
    internal long WorkerInitialBytes { get; }
    internal long HostPeakBytes { get; private set; }
    internal long WorkerPeakBytes { get; private set; }

    internal MemorySampler(int workerPid)
    {
        _worker = Process.GetProcessById(workerPid);
        HostInitialBytes = HostPeakBytes = _host.WorkingSet64;
        WorkerInitialBytes = WorkerPeakBytes = _worker.WorkingSet64;
        _sampling = SampleAsync();
    }

    private async Task SampleAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                _host.Refresh();
                _worker.Refresh();
                HostPeakBytes = Math.Max(HostPeakBytes, _host.WorkingSet64);
                WorkerPeakBytes = Math.Max(WorkerPeakBytes, _worker.WorkingSet64);
                await Task.Delay(10, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            await _sampling;
        }
        finally
        {
            _stop.Dispose();
            _host.Dispose();
            _worker.Dispose();
        }
    }
}
