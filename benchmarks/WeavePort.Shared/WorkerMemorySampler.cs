using System.Diagnostics;

/// <summary>Observes worker RSS until the measurement exits, including failed runs.</summary>
internal sealed class WorkerMemorySampler : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sampling;
    private bool _disposed;
    internal long PeakBytes { get; private set; }

    internal WorkerMemorySampler(Process process)
    {
        _sampling = Task.Run(() => SampleAsync(process));
    }

    private async Task SampleAsync(Process process)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                process.Refresh();
                PeakBytes = Math.Max(PeakBytes, process.WorkingSet64);
                await Task.Delay(Interval, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // The measurement has finished; no additional sample is required.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync();
        try
        {
            await _sampling;
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
