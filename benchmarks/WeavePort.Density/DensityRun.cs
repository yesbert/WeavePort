using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal sealed partial class DensityRun
{
    private readonly DensityConfig _config;
    private readonly PluginHost _scheduled;
    private readonly PluginHost _direct;
    private readonly int _workerLimit;
    private readonly List<IPluginSession> _clients = [];
    private readonly DensityStats[] _stats;
    private readonly CancellationTokenSource _stop = new();
    private string? _failure;
    private DensityResources? _resources;
    private Task _sampling = Task.CompletedTask;
    private double _seconds, _registrationSeconds, _warmupSeconds;
    private long? _measurementStart;
    private SchedulingSnapshot? _before, _after;
    private WorkerPoolSnapshot? _cleanup;

    private DensityRun(DensityConfig config)
    {
        _config = config;
        Directory.CreateDirectory(_config.Output);
        _scheduled = new PluginHost(CreateSchedulingOptions(_config));
        _workerLimit = _config.Workers ?? (int)Math.Min(int.MaxValue, _config.MemoryBudgetMiB / 64);
        _direct = new PluginHost(_workerLimit, new WorkerPoolOptions(MaximumWorkers: _workerLimit,
            MemoryBudgetMiB: _config.MemoryBudgetMiB, MaximumWorkersPerTenant: _workerLimit, MaximumConcurrentStarts: _config.ConcurrentStarts, MaximumPristineWorkers: 0));

        _stats = Enumerable.Range(0, _config.Clients).Select(_ => new DensityStats()).ToArray();
    }

    internal static async Task ExecuteAsync(DensityConfig config)
    {
        config.Validate();
        var run = new DensityRun(config);
        await run.ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        ConsoleCancelEventHandler interrupt = (_, e) => { e.Cancel = true; _stop.Cancel(); };
        Console.CancelKeyPress += interrupt;
        try
        {
            await RegisterAsync();
            await WarmAsync();
            await MeasureAsync();
        }
        catch (Exception error) { _failure = error.GetType().Name + ": " + error.Message; _stop.Cancel(); }
        finally
        {
            await CleanupAsync();
            Console.CancelKeyPress -= interrupt;
            _stop.Dispose();
        }
        await WriteReportAsync();
    }

    private async Task RegisterAsync()
    {
        ExecutionProfile profile = Profile(_config) with
        {
            Reconstructible = true,
            ReusePolicy = _config.ApprovedSdk ? WorkerReusePolicy.ApprovedSessions : WorkerReusePolicy.CustomerBound
        };
        var active = new IPluginSession[_config.Clients];
        long registrationStart = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(Enumerable.Range(0, Math.Max(_config.Clients, _config.RegisteredClients)),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = _stop.Token }, async (i, registrationToken) =>
        {
            var context = new PluginContext("tenant-" + i, "fixture", "1", "density", JsonSerializer.SerializeToElement(new
            {
                data = new string('x', _config.PayloadBytes)
            }));
            var binding = _config.Mode == "scheduled" ? (IPluginSession)await _scheduled.BindAsync(context, profile, new NoCallbacks(), [], cancellationToken: registrationToken) :
                await _direct.BindAsync(context, profile, new NoCallbacks(), [], cancellationToken: registrationToken);
            if (i < _config.Clients)
            {
                active[i] = binding;
            }
        });
        _clients.AddRange(active);
        _registrationSeconds = Stopwatch.GetElapsedTime(registrationStart).TotalSeconds;
    }

    private async Task WarmAsync()
    {
        long warmupStart = Stopwatch.GetTimestamp();
        if (_config.WarmupCustomers > 0)
        {
            await Task.WhenAll(Enumerable.Range(0, _config.WarmupCustomers).Select(async i =>
                            Validate(await _clients[i].InvokeAsync(Operation(_config), Payload(_config)), i, _config.PayloadBytes)));
        }

        _warmupSeconds = Stopwatch.GetElapsedTime(warmupStart).TotalSeconds;
        // Warm only the cache-sized working set. Oversubscribed tenants deliberately expose startup/eviction costs.
        if (_config.Traffic == "population")
        {
            return;
        }
        for (int i = 0; i < Math.Min(_config.Clients, _workerLimit); i++)
        {
            Validate(await _clients[i].InvokeAsync(Operation(_config), Payload(_config)), i, _config.PayloadBytes);
        }
    }

    private async Task MeasureAsync()
    {
        _before = _scheduled.Scheduling;
        _resources = new DensityResources(_config, _stop, _clients, () => _config.Mode == "scheduled" ? _scheduled.Snapshot : _direct.Snapshot);
        _sampling = _resources.RunAsync();
        long start = Stopwatch.GetTimestamp();
        _measurementStart = start;
        await RunTrafficAsync(start);
        _seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        _after = _scheduled.Scheduling;
        _stop.Cancel();
        await _sampling;
        _failure = _resources.Failure;
    }

    private async Task RunTrafficAsync(long start)
    {
        if (_config.Traffic == "population")
        {
            await PopulationAsync(_config, _clients, _stats, _stop, start);
        }
        else if (_config.Rate == 0)
        {
            await ClosedAsync(_config, _clients, _stats, _stop, start);
        }
        else
        {
            await OpenAsync(_config, _clients, _stats, _stop, start);
        }

    }

    private async Task CleanupAsync()
    {
        if (_seconds == 0 && _measurementStart is { } began)
        {
            _seconds = Stopwatch.GetElapsedTime(began).TotalSeconds;
        }

        _stop.Cancel();
        await _sampling;
        try
        {
            await _scheduled.DisposeAsync();
        }
        catch (Exception error) { _failure = "scheduler-cleanup: " + error.GetType().Name; }
        try
        {
            await _direct.DisposeAsync();
        }
        catch (Exception error) { _failure = "direct-cleanup: " + error.GetType().Name; }
        _cleanup = _config.Mode == "scheduled" ? _scheduled.Snapshot : _direct.Snapshot;
    }

    private static SchedulingOptions CreateSchedulingOptions(DensityConfig config)
    {
        return new SchedulingOptions
        {
            MaximumWorkers = config.Workers,
            MemoryBudgetMiB = config.MemoryBudgetMiB,
            MaximumHeavyCalls = config.Workers == 1 ? 0 : 1,
            MaximumConcurrentStarts = config.ConcurrentStarts,
            MaximumRegistrations = Math.Max(10000, Math.Max(config.Clients, config.RegisteredClients)),
            MaximumPristineWorkers = config.Pristine,
            MaximumQueuedCalls = config.MaxPending,
            MaximumQueuedCallsPerTenant = config.MaxPending,
            QueueTimeout = TimeSpan.FromMilliseconds(config.QueueMs),
            NormalTimeout = TimeSpan.FromSeconds(10),
            IdleTimeout = TimeSpan.FromMilliseconds(config.IdleMs)
        };
    }
}
