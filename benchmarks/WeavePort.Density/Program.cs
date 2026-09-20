using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

if (args.SequenceEqual(["--observer-check"]))
{
    string first = "weaveport-" + new string('a', 32), second = "weaveport-" + new string('b', 32);
    string commands = $"/opt/homebrew/bin/docker run -i --name {first} image\n/docker run --name={second} image\n/docker run --name other-service image\n/docker rm {first}\n/docker run --name weaveport-invalid image\n";
    if (!DensityResources.ExtractDockerInstances(commands).ToHashSet().SetEquals(new[] { first, second }))
        throw new InvalidDataException("Owned pristine Docker name extraction failed");
    Console.WriteLine("PASS owned Docker name extraction includes pristine/startup names and excludes unrelated names");
    return;
}
if (args.SequenceEqual(["--histogram-check"])) { HistogramChecks.Run(); return; }
if (args.Length != 1) throw new ArgumentException("Pass one JSON configuration file.");
var config = JsonSerializer.Deserialize<DensityConfig>(await File.ReadAllTextAsync(args[0]))!;
await DensityRun.ExecuteAsync(config);

internal sealed record DensityConfig
{
    public string Root { get; init; } = "";
    public string Output { get; init; } = "";
    public string Adapter { get; init; } = "native";
    public string Language { get; init; } = "python";
    public string? DockerImage { get; init; }
    public string Executable { get; init; } = "/usr/bin/python3";
    public string Mode { get; init; } = "scheduled";
    public bool ApprovedSdk { get; init; }
    public string[]? Arguments { get; init; }
    public int Clients { get; init; } = 8;
    public int RegisteredClients { get; init; }
    public int? Workers { get; init; }
    public long MemoryBudgetMiB { get; init; } = 4096;
    public int WorkerMemoryMiB { get; init; } = 64;
    public int ConcurrentStarts { get; init; } = 8;
    public int Seconds { get; init; } = 10;
    public double Rate { get; init; }
    public string Traffic { get; init; } = "saturation";
    public double CallsPerCustomerPerMinute { get; init; } = 1;
    public int Seed { get; init; } = 1729;
    public int PayloadBytes { get; init; } = 64;
    public int WarmupCustomers { get; init; }
    public int Pristine { get; init; }
    public int P99Ms { get; init; } = 1000;
    public int QueueMs { get; init; } = 5000;
    public int MaxHostRssMiB { get; init; } = 1536;
    public int MaxOwnedRssMiB { get; init; } = 4096;
    public int MaxPending { get; init; } = 4096;
    public int IdleMs { get; init; } = 120000;
}

internal static class DensityRun
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    internal static async Task ExecuteAsync(DensityConfig config)
    {
        if (config.Clients is < 1 or > 100000 || config.RegisteredClients is < 0 or > 100000 ||
            config.RegisteredClients != 0 && config.RegisteredClients < config.Clients || config.Workers is < 1 || config.MemoryBudgetMiB < 64 || config.WorkerMemoryMiB < 64 || config.WorkerMemoryMiB > config.MemoryBudgetMiB || config.ConcurrentStarts < 1 || config.Seconds is < 1 or > 600 ||
            config.WarmupCustomers < 0 || config.WarmupCustomers > config.Clients || config.Rate < 0 || config.Rate > 100000 || config.PayloadBytes is < 0 or > 65536 || config.Mode is not ("scheduled" or "direct") ||
            config.Adapter is not ("native" or "docker") || config.MaxPending is < 1 or > 10000 ||
            config.Traffic is not ("saturation" or "population") || config.CallsPerCustomerPerMinute is <= 0 or > 60 ||
            config.Traffic == "population" && config.Seconds < 60 / config.CallsPerCustomerPerMinute)
            throw new ArgumentException("Configuration outside bounded density experiment limits.");
        Directory.CreateDirectory(config.Output);
        var options = new SchedulingOptions
        {
            MaximumWorkers = config.Workers, MemoryBudgetMiB = config.MemoryBudgetMiB, MaximumHeavyCalls = config.Workers == 1 ? 0 : 1,
            MaximumConcurrentStarts = config.ConcurrentStarts, MaximumRegistrations = Math.Max(10000, Math.Max(config.Clients, config.RegisteredClients)),
            MaximumPristineWorkers = config.Pristine, MaximumQueuedCalls = config.MaxPending,
            MaximumQueuedCallsPerTenant = config.MaxPending, QueueTimeout = TimeSpan.FromMilliseconds(config.QueueMs),
            NormalTimeout = TimeSpan.FromSeconds(10), IdleTimeout = TimeSpan.FromMilliseconds(config.IdleMs)
        };
        var scheduled = new ScheduledPluginHost(options);
        int workerLimit = config.Workers ?? (int)Math.Min(int.MaxValue, config.MemoryBudgetMiB / 64);
        var direct = new PluginHost(workerLimit, new WorkerPoolOptions(MaximumWorkers: workerLimit,
            MemoryBudgetMiB: config.MemoryBudgetMiB, MaximumWorkersPerTenant: workerLimit, MaximumConcurrentStarts: config.ConcurrentStarts, MaximumPristineWorkers: 0));
        var clients = new List<IPluginSession>();
        var stats = Enumerable.Range(0, config.Clients).Select(_ => new DensityStats()).ToArray();
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler interrupt = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += interrupt;
        string? failure = null;
        DensityResources? resources = null;
        Task sampling = Task.CompletedTask;
        double seconds = 0;
        double registrationSeconds = 0;
        double warmupSeconds = 0;
        long? measurementStart = null;
        SchedulingSnapshot? before = null, after = null;
        WorkerPoolSnapshot? cleanup = null;
        try
        {
            ExecutionProfile profile = Profile(config) with { ReusePolicy = config.ApprovedSdk ? WorkerReusePolicy.ApprovedSessions : WorkerReusePolicy.CustomerBound };
            var active = new IPluginSession[config.Clients];
            long registrationStart = Stopwatch.GetTimestamp();
            await Parallel.ForEachAsync(Enumerable.Range(0, Math.Max(config.Clients, config.RegisteredClients)),
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = stop.Token }, async (i, registrationToken) =>
            {
                var context = new PluginContext("tenant-" + i, "fixture", "1", "density", JsonSerializer.SerializeToElement(new { data = new string('x', config.PayloadBytes) }));
                var binding = config.Mode == "scheduled" ? (IPluginSession)await scheduled.RegisterAsync(context, profile, new NoCallbacks(), [], cancellationToken: registrationToken) :
                    await direct.BindAsync(context, profile, new NoCallbacks(), [], cancellationToken: registrationToken);
                if (i < config.Clients) active[i] = binding;
            });
            clients.AddRange(active);
            registrationSeconds = Stopwatch.GetElapsedTime(registrationStart).TotalSeconds;
            long warmupStart = Stopwatch.GetTimestamp();
            if (config.WarmupCustomers > 0)
                await Task.WhenAll(Enumerable.Range(0, config.WarmupCustomers).Select(async i =>
                    Validate(await clients[i].InvokeAsync(Operation(config), Payload(config)), i, config.PayloadBytes)));
            warmupSeconds = Stopwatch.GetElapsedTime(warmupStart).TotalSeconds;
            // Warm only the cache-sized working set. Oversubscribed tenants deliberately expose startup/eviction costs.
            if (config.Traffic != "population")
                for (int i = 0; i < Math.Min(config.Clients, workerLimit); i++) Validate(await clients[i].InvokeAsync(Operation(config), Payload(config)), i, config.PayloadBytes);
            before = scheduled.Snapshot;
            resources = new DensityResources(config, stop, clients, () => config.Mode == "scheduled" ? scheduled.Snapshot.Runtime : direct.Snapshot);
            sampling = resources.RunAsync();
            long start = Stopwatch.GetTimestamp();
            measurementStart = start;
            if (config.Traffic == "population") await PopulationAsync(config, clients, stats, stop, start);
            else if (config.Rate == 0) await ClosedAsync(config, clients, stats, stop, start);
            else await OpenAsync(config, clients, stats, stop, start);
            seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            after = scheduled.Snapshot;
            stop.Cancel();
            await sampling;
            failure = resources.Failure;
        }
        catch (Exception error) { failure = error.GetType().Name + ": " + error.Message; stop.Cancel(); }
        finally
        {
            if (seconds == 0 && measurementStart is { } began) seconds = Stopwatch.GetElapsedTime(began).TotalSeconds;
            stop.Cancel();
            await sampling;
            try { await scheduled.DisposeAsync(); } catch (Exception error) { failure = "scheduler-cleanup: " + error.GetType().Name; }
            try { await direct.DisposeAsync(); } catch (Exception error) { failure = "direct-cleanup: " + error.GetType().Name; }
            cleanup = config.Mode == "scheduled" ? scheduled.Snapshot.Runtime : direct.Snapshot;
            Console.CancelKeyPress -= interrupt;
        }
        var total = DensityStats.Combine(stats);
        double throughput = seconds > 0 ? total.Success / seconds : 0;
        bool passed = failure is null && total.Errors == 0 && stats.All(s => s.Success > 0 && s.Latency.Quantile(.99) <= config.P99Ms) && total.Latency.Quantile(.99) <= config.P99Ms &&
            cleanup is { Workers: 0, Bindings: 0, Tenants: 0, MaintenanceFailure: null } && after?.Failure is null;
        var report = new
        {
            passed, failure, config, seconds, registrationSeconds, warmupSeconds, throughput,
            offeredRate = config.Traffic == "population" ? config.Clients * config.CallsPerCustomerPerMinute / 60 : config.Rate,
            totals = total.Report(), tenants = stats.Select(s => s.Report()),
            before, after, cleanup, resources = resources?.Summary(),
            identity = new { os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = Environment.Version.ToString(), cpuCount = Environment.ProcessorCount, serverGc = GCSettings.IsServerGC,
                hostingSha256 = Hash(typeof(PluginHost).Assembly.Location), harnessSha256 = Hash(typeof(DensityRun).Assembly.Location),
                scope = "Source-built candidate, native trusted processes or local Docker stdio. Population and positive Rate use fixed intended arrivals; no M2 Ultra comparison." }
        };
        await File.WriteAllTextAsync(Path.Combine(config.Output, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { passed, config.Adapter, config.Mode, config.Clients, config.Workers,
            rps = throughput, p99 = total.Latency.Quantile(.99), errors = total.Errors, failure, resources = resources?.Summary() }));
        if (!passed) Environment.ExitCode = 1;
    }

    private static ExecutionProfile Profile(DensityConfig config)
    {
        if (config.Adapter == "docker") return new DockerProfile(config.DockerImage ?? "weaveport-poc-" + config.Language + ":1", MemoryMiB: config.WorkerMemoryMiB, Timeout: TimeSpan.FromSeconds(10));
        string script = config.Language switch
        {
            "python" => "plugins/python/worker.py",
            "typescript" => "plugins/typescript/worker.ts",
            "csharp" => "plugins/csharp/bin/Release/net10.0/WeavePort.SamplePlugin.dll",
            _ => throw new ArgumentException("Unknown language")
        };
        return new ProcessProfile(config.Executable, config.Arguments ?? [Path.Combine(config.Root, script)], trustedCode: true,
            workspaceRoot: Path.Combine(config.Output, "workers"), reservedMemoryMiB: config.WorkerMemoryMiB, timeout: TimeSpan.FromSeconds(10));
    }

    private static async Task ClosedAsync(DensityConfig config, List<IPluginSession> clients, DensityStats[] stats, CancellationTokenSource stop, long start)
    {
        await Task.WhenAll(clients.Select((client, i) => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested && Stopwatch.GetElapsedTime(start).TotalSeconds < config.Seconds)
                await InvokeAsync(client, i, stats[i], config, stop.Token, Stopwatch.GetTimestamp());
        })));
    }

    private static async Task OpenAsync(DensityConfig config, List<IPluginSession> clients, DensityStats[] stats, CancellationTokenSource stop, long start)
    {
        var pending = new List<Task>();
        long count = (long)(config.Rate * config.Seconds);
        try
        {
            for (long n = 0; n < count && !stop.IsCancellationRequested; n++)
            {
                double due = n / config.Rate;
                double remaining;
                while ((remaining = due - Stopwatch.GetElapsedTime(start).TotalSeconds) > 0)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(.001, remaining)), stop.Token);
                int lane = (int)(n % clients.Count);
                long intended = start + (long)(due * Stopwatch.Frequency);
                pending.RemoveAll(task => task.IsCompleted);
                if (pending.Count >= config.MaxPending) { lock (stats[lane]) stats[lane].Dropped++; continue; }
                pending.Add(InvokeAsync(clients[lane], lane, stats[lane], config, stop.Token, intended));
            }
        }
        finally
        {
            await Task.WhenAll(pending);
        }
    }

    private static async Task PopulationAsync(DensityConfig config, List<IPluginSession> clients, DensityStats[] stats, CancellationTokenSource stop, long start)
    {
        double period = 60 / config.CallsPerCustomerPerMinute;
        var random = new Random(config.Seed);
        var arrivals = new List<(double Due, int Lane)>();
        for (int lane = 0; lane < clients.Count; lane++)
            for (double due = random.NextDouble() * period; due < config.Seconds; due += period)
                arrivals.Add((due, lane));
        arrivals.Sort((a, b) => a.Due.CompareTo(b.Due));
        var pending = new List<Task>();
        try
        {
            foreach (var arrival in arrivals)
            {
                stop.Token.ThrowIfCancellationRequested();
                double remaining;
                while ((remaining = arrival.Due - Stopwatch.GetElapsedTime(start).TotalSeconds) > 0)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(.001, remaining)), stop.Token);
                pending.RemoveAll(task => task.IsCompleted);
                if (pending.Count >= config.MaxPending) { lock (stats[arrival.Lane]) stats[arrival.Lane].Dropped++; continue; }
                long intended = start + (long)(arrival.Due * Stopwatch.Frequency);
                pending.Add(InvokeAsync(clients[arrival.Lane], arrival.Lane, stats[arrival.Lane], config, stop.Token, intended));
            }
            double tail = config.Seconds - Stopwatch.GetElapsedTime(start).TotalSeconds;
            if (tail > 0) await Task.Delay(TimeSpan.FromSeconds(tail), stop.Token);
        }
        finally { await Task.WhenAll(pending); }
    }

    private static async Task InvokeAsync(IPluginSession client, int lane, DensityStats stats, DensityConfig config, CancellationToken token, long intended)
    {
        long actual = Stopwatch.GetTimestamp();
        ScheduledInvocationResult measured = client is ScheduledPlugin scheduled ? await scheduled.InvokeMeasuredAsync(Operation(config), Payload(config), token) :
            new(await client.InvokeAsync(Operation(config), Payload(config), token), 0, Stopwatch.GetElapsedTime(actual).TotalMilliseconds, false);
        double elapsed = Stopwatch.GetElapsedTime(intended).TotalMilliseconds;
        lock (stats)
        {
            stats.ArrivalLag.Add(Stopwatch.GetElapsedTime(intended, actual).TotalMilliseconds);
            stats.Latency.Add(elapsed);
            if (client is ScheduledPlugin) (measured.Cold ? stats.ColdLatency : stats.ResidentLatency).Add(elapsed);
            stats.Queue.Add(measured.QueueMs);
            stats.Execution.Add(measured.ExecutionMs);
            stats.Cold += measured.Cold ? 1 : 0;
            try { Validate(measured.Result, lane, config.PayloadBytes); stats.Success++; }
            catch (InvalidDataException) { stats.Failed++; stats.Statuses[measured.Result.Status] = stats.Statuses.GetValueOrDefault(measured.Result.Status) + 1; }
        }
    }

    private static string Operation(DensityConfig config) => config.ApprovedSdk ? "$sdk.call" : "context";
    private static readonly JsonElement SdkPayload = JsonSerializer.SerializeToElement(new { operation = "context", input = new {} });
    private static JsonElement Payload(DensityConfig config) => config.ApprovedSdk ? SdkPayload : Empty;

    private static void Validate(InvocationResult result, int lane, int bytes)
    {
        if (result.Status != "ok" || result.Value.GetProperty("tenant").GetString() != "tenant-" + lane ||
            result.Value.GetProperty("configuration").GetProperty("data").GetString() != new string('x', bytes))
            throw new InvalidDataException("Incomplete result, tenant mismatch or invocation failure: " + result.Status);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class NoCallbacks : IHostCallbacks
    { public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new InvalidOperationException("No callbacks granted"); }
}
