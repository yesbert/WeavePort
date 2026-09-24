using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal sealed partial class DensityRun
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static ExecutionProfile Profile(DensityConfig config)
    {
        if (config.Adapter == "docker")
        {
            return new DockerProfile(config.DockerImage ?? "weaveport-poc-" + config.Language + ":1", MemoryMiB: config.WorkerMemoryMiB, Timeout: TimeSpan.FromSeconds(10));
        }

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
            {
                await InvokeAsync(client, i, stats[i], config, stop.Token, Stopwatch.GetTimestamp());
            }
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
                await WaitUntilAsync(due, start, stop.Token);

                int lane = (int)(n % clients.Count);
                long intended = start + (long)(due * Stopwatch.Frequency);
                pending.RemoveAll(task => task.IsCompleted);
                if (pending.Count >= config.MaxPending)
                {
                    lock (stats[lane])
                    {
                        stats[lane].Dropped++;
                    }
                    continue;
                }
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
        {
            AddCustomerArrivals(arrivals, lane, random.NextDouble() * period, period, config.Seconds);
        }

        arrivals.Sort((a, b) => a.Due.CompareTo(b.Due));
        var pending = new List<Task>();
        try
        {
            foreach (var arrival in arrivals)
            {
                stop.Token.ThrowIfCancellationRequested();
                await WaitUntilAsync(arrival.Due, start, stop.Token);

                pending.RemoveAll(task => task.IsCompleted);
                if (pending.Count >= config.MaxPending)
                {
                    lock (stats[arrival.Lane])
                    {
                        stats[arrival.Lane].Dropped++;
                    }
                    continue;
                }
                long intended = start + (long)(arrival.Due * Stopwatch.Frequency);
                pending.Add(InvokeAsync(clients[arrival.Lane], arrival.Lane, stats[arrival.Lane], config, stop.Token, intended));
            }
            double tail = config.Seconds - Stopwatch.GetElapsedTime(start).TotalSeconds;
            if (tail > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(tail), stop.Token);
            }
        }
        finally { await Task.WhenAll(pending); }
    }

    private static async Task WaitUntilAsync(double dueSeconds, long start, CancellationToken token)
    {
        double remaining;
        while ((remaining = dueSeconds - Stopwatch.GetElapsedTime(start).TotalSeconds) > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(.001, remaining)), token);
        }
    }

    private static void AddCustomerArrivals(List<(double Due, int Lane)> arrivals, int lane,
        double firstDue, double period, int durationSeconds)
    {
        for (double due = firstDue; due < durationSeconds; due += period)
        {
            arrivals.Add((due, lane));
        }
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
            if (client is ScheduledPlugin)
            {
                (measured.Cold ? stats.ColdLatency : stats.ResidentLatency).Add(elapsed);
            }

            stats.Queue.Add(measured.QueueMs);
            stats.Execution.Add(measured.ExecutionMs);
            stats.Cold += measured.Cold ? 1 : 0;
            try
            {
                Validate(measured.Result, lane, config.PayloadBytes);
                stats.Success++;
            }
            catch (InvalidDataException) { stats.Failed++; stats.Statuses[measured.Result.Status] = stats.Statuses.GetValueOrDefault(measured.Result.Status) + 1; }
        }
    }

    private static string Operation(DensityConfig config) => config.ApprovedSdk ? "$sdk.call" : "context";
    private static readonly JsonElement SdkPayload = JsonSerializer.SerializeToElement(new { operation = "context", input = new { } });
    private static JsonElement Payload(DensityConfig config) => config.ApprovedSdk ? SdkPayload : Empty;

    private static void Validate(InvocationResult result, int lane, int bytes)
    {
        if (result.Status != "ok" || result.Value.GetProperty("tenant").GetString() != "tenant-" + lane ||
            result.Value.GetProperty("configuration").GetProperty("data").GetString() != new string('x', bytes))
        {
            throw new InvalidDataException("Incomplete result, tenant mismatch or invocation failure: " + result.Status);
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new InvalidOperationException("No callbacks granted");
    }
}
