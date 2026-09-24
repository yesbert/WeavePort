using WeavePort.Internal;

namespace WeavePort.Hosting;
internal sealed partial class PluginScheduler
{
    private readonly Dictionary<(ExecutionProfile Profile, string Version), int> _reserve = [];
    private long? _lastReserve;
    private async Task MaintainReserveAsync()
    {
        Dictionary<(ExecutionProfile Profile, string Version), int> desired;
        lock (_sync)
        {
            if (_closed || _failure is not null || _queue.Count != 0 || _active.Count != 0 || _lastReserve is { } last && _clock.GetElapsedTime(last) < TimeSpan.FromSeconds(1))
            {
                return;
            }

            _lastReserve = _clock.GetTimestamp();
            desired = _plugins.Values.Where(p => !p.Closed && p.LastDemand is { } demand && _clock.GetElapsedTime(demand) < _options.DemandWindow).OrderByDescending(p => p.LastDemand).Select(p => (Profile: p.Profile.Normalize(), p.Version)).Distinct().Take(Math.Min(_workerLimit, _options.MaximumPristineWorkers)).ToDictionary(key => key, _ => 1);
        }

        foreach (var key in _reserve.Keys.Except(desired.Keys).ToArray())
        {
            await _host.PrewarmAsync(key.Profile, key.Version, 0, cancellationToken: _lifetime.Token);
            _reserve.Remove(key);
        }

        foreach (var key in desired.Keys.Except(_reserve.Keys))
        {
            await _host.PrewarmAsync(key.Profile, key.Version, 1, cancellationToken: _lifetime.Token);
            _reserve[key] = 1;
        }
    }

    internal Task RestartAsync(ScheduledPlugin plugin, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closed || plugin.Closed, plugin);
            if (plugin.Active)
            {
                throw new InvalidOperationException("Cannot restart while work or cleanup is active.");
            }

            SetActive(plugin, true);
            plugin.Idle = ScheduledPlugin.NewSignal();
            return ReleaseResidentAsync(plugin, token);
        }
    }

    private async Task ReleaseResidentAsync(ScheduledPlugin plugin, CancellationToken token)
    {
        try
        {
            await plugin.Session.RestartAsync(token);
            lock (_sync)
            {
                SetResident(plugin, false);
                _evictions++;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Cancellation before restart acquires the gate leaves the resident reservation intact.
            throw;
        }
        catch (Exception error)
        {
            lock (_sync)
            {
                _failure = error.GetType().Name;
                RejectPending(FailureCodes.Busy);
            }

            throw;
        }
        finally
        {
            lock (_sync)
            {
                SetActive(plugin, false);
                plugin.Idle.TrySetResult();
                Wake();
            }
        }
    }

    internal Task RemoveAsync(ScheduledPlugin plugin)
    {
        lock (_sync)
        {
            plugin.Closed = true;
            ExpirePending();
            return plugin.Disposal ??= Task.Run(() => RemoveCoreAsync(plugin));
        }
    }

    private async Task RemoveCoreAsync(ScheduledPlugin plugin)
    {
        try
        {
            await Cleanup.RunAsync(() => plugin.Session.DisposeAsync().AsTask(), () => plugin.Idle.Task);
        }
        catch (Exception error)
        {
            lock (_sync)
            {
                _failure = error.GetType().Name;
                RejectPending(FailureCodes.Busy);
            }

            throw;
        }
        finally
        {
            lock (_sync)
            {
                _plugins.Remove((plugin.Tenant, plugin.Plugin));
                SetActive(plugin, false);
                SetResident(plugin, false);
                if (--_tenantRegistrations[plugin.Tenant] == 0)
                {
                    _tenantRegistrations.Remove(plugin.Tenant);
                    _turns.Remove(plugin.Tenant);
                }

                Wake();
            }
        }
    }

    /// <summary>Closes the queue, cancels active work, drains cleanup and preserves any failure.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closed = true;
            RejectPending(FailureCodes.Disabled);
            return new(_disposal ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await Cleanup.RunAsync(() => _lifetime.CancelAsync(), async () =>
            {
                await _pump;
                ScheduledPlugin[] plugins;
                lock (_sync)
                {
                    plugins = _plugins.Values.ToArray();
                }

                await Task.WhenAll(plugins.Select(RemoveAsync));
            });
        }
        finally
        {
            _lifetime.Dispose();
        }
    }
}
