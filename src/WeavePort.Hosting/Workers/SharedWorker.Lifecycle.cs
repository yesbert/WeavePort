namespace WeavePort.Hosting;

internal sealed partial class SharedWorker
{
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMilliseconds(100);

    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(WatchdogInterval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                bool silent;
                lock (_sync)
                {
                    silent = _calls.Count > 0 && clock.GetElapsedTime(_lastFrame) >= plugin.Options.SilenceTimeout;
                }

                if (!silent)
                {
                    continue;
                }

                Retire();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Owned shutdown ends the watchdog; the reader performs channel retirement.
        }
    }

    internal async Task DrainAsync(TimeSpan timeout)
    {
        Task[] pending;
        lock (_sync)
        {
            _ready = false;
            pending = _calls.Values.Select(c => c.Completion.Task).ToArray();
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            // The drain grace expired. The owner proceeds to disposal, which retires
            // the worker and completes any remaining calls through the reader.
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            return new(_disposal ??= Task.Run(DisposeCoreAsync, CancellationToken.None));
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await WeavePort.Internal.Cleanup.RunAsync(() => _lifetime.CancelAsync(), ReleaseAsync);
        }
        finally
        {
            _channelStop?.Dispose();
            _lifetime.Dispose();
        }
    }

    private async Task ReleaseAsync()
    {
        Retire();
        try
        {
            if (_reader is not null)
            {
                await _reader;
            }

            if (_watch is not null)
            {
                await _watch;
            }
        }
        finally
        {
            if (_worker is not null)
            {
                await pool.DestroyAsync(_worker);
            }
        }
    }
}
