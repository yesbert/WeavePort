using WeavePort.Internal;

namespace WeavePort.Hosting;

public sealed partial class PluginHost
{
    /// <summary>Runs idle release, quarantine cleanup and pristine replenishment. Also runs periodically.</summary>
    public async Task MaintainAsync(CancellationToken cancellationToken = default)
    {
        PluginSession[] sessions;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            sessions = _sessions.ToArray();
        }

        foreach (PluginSession session in sessions)
        {
            await session.ReleaseIdleAsync(cancellationToken);
        }

        await _pool.MaintainAsync(cancellationToken);
    }

    private async Task MaintainLoopAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval, _clock);
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                try
                {
                    await MaintainAsync(_lifetime.Token);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    lock (_sync)
                    {
                        _maintenanceFailure = error.GetType().Name;
                    }

                    RuntimeLog.MaintenanceFailed(_logger, error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Cancellation is the normal end of the owned maintenance loop.
        }
    }

    /// <summary>Disables bindings, stops maintenance and attempts all worker removals. Unconfirmed cleanup is reported.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            return new ValueTask(_disposal ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        PluginSession[] sessions;
        lock (_sync)
        {
            _disposed = true;
            _diagnostics?.Complete();
            sessions = _sessions.ToArray();
        }

        try
        {
            await Cleanup.RunAsync(() => _lifetime.CancelAsync(), () => ReleaseResourcesAsync(sessions));
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ReleaseResourcesAsync(PluginSession[] sessions)
    {
        List<Exception> errors = [];
        SharedPlugin[] shared;
        lock (_sync)
        {
            shared = _shared.ToArray();
        }

        await DisposeSharedAsync(shared, errors);
        try
        {
            await _maintenance;
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        if (_scheduler is not null)
        {
            try
            {
                await _scheduler.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        foreach (PluginSession session in sessions)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        try
        {
            await _pool.DisposeAsync();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Host cleanup was incomplete.", errors);
        }
    }

    private static async Task DisposeSharedAsync(SharedPlugin[] shared, List<Exception> errors)
    {
        foreach (var plugin in shared)
        {
            try
            {
                await plugin.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
    }
}
