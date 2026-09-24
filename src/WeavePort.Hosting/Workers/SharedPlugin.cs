using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;
internal sealed class SharedPlugin(PluginHost host, SessionBinding binding, SharedWorkerOptions options, WorkerPool pool, TimeProvider clock) : ISharedPlugin
{
    private readonly object _sync = new();
    private readonly List<SharedWorker> _workers = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<long> _restarts = new();
    private bool _disabled;
    private string? _failure;
    private Task? _disposal;
    private Task? _startup;
    internal ProcessProfile Profile => (ProcessProfile)binding.Profile;
    internal PluginContext Context => binding.Context;
    internal IHostCallbacks Callbacks => binding.Callbacks;
    internal HashSet<string> Grants => binding.Grants;
    internal SharedWorkerOptions Options => options;
    internal PluginHost Host => host;
    internal CancellationToken Lifetime => _lifetime.Token;

    public SharedPluginSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(_workers.Count(w => w.Ready), _workers.Sum(w => w.Active), _workers.Sum(w => w.Abandoned), _restarts.Count, _disabled)
                {
                    Failure = _failure
                };
            }
        }
    }

    internal Task StartAsync(CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
            return _startup ??= Task.Run(() => StartCoreAsync(token), CancellationToken.None);
        }
    }

    private async Task StartCoreAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        for (int i = 0; i < options.Workers; i++)
        {
            var worker = new SharedWorker(this, pool, clock);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposal is not null, this);
                _workers.Add(worker);
            }

            await worker.StartAsync(linked.Token);
        }
    }

    public IBoundPluginClient For(string tenant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposal is not null, this);
        }

        return new LocalPluginClient(new SharedView(this, tenant));
    }

    internal bool Available
    {
        get
        {
            lock (_sync)
            {
                return !_disabled && _workers.Any(w => w.Available);
            }
        }
    }

    internal SharedWorker? Reserve()
    {
        lock (_sync)
        {
            if (_disabled || _disposal is not null)
            {
                return null;
            }

            var worker = _workers.Where(w => w.Available).OrderBy(w => w.Active).FirstOrDefault();
            return worker?.TryReserve() == true ? worker : null;
        }
    }

    internal void Disable(string failure)
    {
        lock (_sync)
        {
            _disabled = true;
            _failure = failure;
        }

        host.NotifySharedCapacity();
    }

    internal bool AllowRestart()
    {
        lock (_sync)
        {
            if (_disposal is not null || _disabled)
            {
                return false;
            }

            while (_restarts.TryPeek(out long first) && clock.GetElapsedTime(first) > options.RestartWindow)
            {
                _restarts.Dequeue();
            }

            if (_restarts.Count >= options.MaximumRestarts)
            {
                _disabled = true;
                return false;
            }

            _restarts.Enqueue(clock.GetTimestamp());
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            return new(_disposal ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        lock (_sync)
        {
            _disabled = true;
        }

        try
        {
            if (_startup is not null && !_startup.IsCompleted)
            {
                await _lifetime.CancelAsync();
                try
                {
                    await _startup;
                }
                catch (Exception)
                { /* Startup's caller observes its failure; disposal still owns every created worker. */
                }
            }

            SharedWorker[] workers;
            lock (_sync)
            {
                workers = _workers.ToArray();
            }

            await Task.WhenAll(workers.Select(w => w.DrainAsync(Profile.Timeout ?? TimeSpan.FromSeconds(5))));
            await WeavePort.Internal.Cleanup.RunAsync(() => _lifetime.CancelAsync(), () => Task.WhenAll(workers.Select(w => w.DisposeAsync().AsTask())));
        }
        finally
        {
            host.RemoveShared(this);
            _lifetime.Dispose();
        }
    }
}
