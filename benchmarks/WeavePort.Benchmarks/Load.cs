using System.Diagnostics;
using System.Text.Json;
using WeavePort.SdkFixture;
using WeavePort.Sdk.Client;

namespace WeavePort.Benchmarks;

internal static class Load
{
    internal static async Task MeasureAsync(string language, bool remote, string scenario, int concurrency, int repeat, int durationSeconds = 3)
    {
        if (durationSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }

        Console.WriteLine("Creating harness");
        await using var harness = await Harness.CreateAsync(Enumerable.Repeat(language, concurrency).ToArray(), remote);
        Console.WriteLine("Warming bindings");
        for (int lane = 0; lane < concurrency; lane++)
        {
            await WarmBindingAsync(harness.Clients[lane], scenario, "tenant-" + lane);
        }

        Console.WriteLine("Reading worker identities");
        int[] workers = await harness.WorkerIdsAsync();
        if (workers.Length != concurrency)
        {
            throw new InvalidDataException("Worker count mismatch.");
        }

        Console.WriteLine("Starting measurement");
        using var stop = new CancellationTokenSource();
        var samples = new List<Resources>();
        var clock = Stopwatch.StartNew();
        Resources before = Resources.Capture(workers, harness.GatewayId, 0);
        samples.Add(before);
        Task sampler = SampleAsync(workers, harness.GatewayId, samples, clock, stop.Token);
        long end = Stopwatch.GetTimestamp() + durationSeconds * Stopwatch.Frequency;
        long allocated = GC.GetTotalAllocatedBytes();
        Lane[] lanes;
        Resources after;
        double seconds;
        try
        {
            Task<Lane>[] pending = harness.Clients.Select((client, lane) =>
                Task.Run(() => RunLaneAsync(client, scenario, "tenant-" + lane, end))).ToArray();
            Console.WriteLine("Awaiting lanes");
            lanes = await Task.WhenAll(pending);
            Console.WriteLine("Lanes completed");
            seconds = clock.Elapsed.TotalSeconds;
            allocated = GC.GetTotalAllocatedBytes() - allocated;
            after = Resources.Capture(workers, harness.GatewayId, seconds);
        }
        finally
        {
            Console.WriteLine("Stopping sampler");
            stop.Cancel();
            await sampler;
        }
        samples.Add(after);
        var measurement = new Measurement(seconds, allocated, before, after, lanes, samples);
        await WriteReportAsync(new RunIdentity(language, remote, scenario, repeat, durationSeconds), harness.GatewayId, workers, measurement);
    }

    private static async Task WriteReportAsync(RunIdentity identity, int gateway, int[] workers, Measurement measurement)
    {
        var (seconds, allocated, before, after, lanes, samples) = measurement;
        long count = lanes.Sum(lane => lane.Count);
        object report = CreateReport(identity, gateway, workers, measurement);
        Console.WriteLine($"Writing report: lanes={lanes.Length}, samples={samples.Count}, calls={count}, managedBytes={GC.GetTotalMemory(false)}, peakHostRss={samples.Max(s => s.HostRss)}, peakGatewayRss={samples.Max(s => s.GatewayRss)}");
        foreach (var lane in lanes)
        {
            Console.WriteLine($"Lane: count={lane.Count}, errors={lane.Errors}, errorLength={lane.FirstError?.Length}, error={lane.FirstError?[..Math.Min(160, lane.FirstError.Length)]}");
        }

        await using (var reportFile = File.Create(Path.Combine(Harness.Output, "load.json")))
        {
            await JsonSerializer.SerializeAsync(reportFile, report);
        }

        if (lanes.Any(l => l.Count == 0 || l.Errors > 0))
        {
            throw new InvalidOperationException("SDK load failed.");
        }

    }

    private static object CreateReport(RunIdentity identity, int gateway, int[] workers, Measurement measurement)
    {
        var (language, remote, scenario, repeat, durationSeconds) = identity;
        var (seconds, allocated, before, after, lanes, samples) = measurement;
        long count = lanes.Sum(l => l.Count);
        long[] histogram = new long[Lane.Buckets];
        foreach (var lane in lanes)
        {
            lane.AddTo(histogram);
        }

        return new
        {
            language,
            topology = remote ? "gateway" : "local",
            scenario,
            concurrency = lanes.Length,
            repeat,
            durationSeconds,
            seconds,
            count,
            errors = lanes.Sum(l => l.Errors),
            rps = count / seconds,
            p50Ms = Lane.Quantile(histogram, .5),
            p99Ms = Lane.Quantile(histogram, .99),
            allocated,
            hostCpuSeconds = after.HostCpu - before.HostCpu,
            gatewayCpuSeconds = after.GatewayCpu - before.GatewayCpu,
            workerCpuSeconds = after.WorkerCpu - before.WorkerCpu,
            peakHostRss = samples.Max(s => s.HostRss),
            peakGatewayRss = samples.Max(s => s.GatewayRss),
            peakWorkerRss = samples.Max(s => s.WorkerRss),
            peakCombinedRss = samples.Max(s => s.HostRss + s.GatewayRss + s.WorkerRss),
            workers,
            gateway,
            lanes = lanes.Select(lane => new
            {
                lane.Count,
                lane.Errors,
                lane.FirstError,
                p50Ms = Lane.Quantile(lane.Histogram, .5),
                p99Ms = Lane.Quantile(lane.Histogram, .99)
            }),
            samples
        };
    }

    private static async Task WarmBindingAsync(IPluginClient client, string scenario, string tenant)
    {
        const int warmupCalls = 2;
        for (int call = 0; call < warmupCalls; call++)
        {
            Workload.Check(await Workload.ExecuteAsync(client, scenario), scenario, tenant);
        }
    }

    private static async Task<Lane> RunLaneAsync(IPluginClient client, string scenario, string tenant, long end)
    {
        var result = new Lane();
        while (Stopwatch.GetTimestamp() < end)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                var output = await Workload.ExecuteAsync(client, scenario);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                Workload.Check(output, scenario, tenant);
                result.Record(elapsed);
            }
            catch (Exception error)
            {
                result.Errors++;
                result.FirstError = error.GetType().Name + ": " + error.Message;
                break;
            }
        }
        return result;
    }

    private static async Task SampleAsync(int[] workers, int gateway, List<Resources> samples,
        Stopwatch clock, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
                samples.Add(Resources.Capture(workers, gateway, clock.Elapsed.TotalSeconds));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The measurement owns this sampler and joins it before reading the samples.
        }
    }

    private sealed record RunIdentity(string Language, bool Remote, string Scenario, int Repeat, int DurationSeconds);

    private sealed record Measurement(double Seconds, long Allocated, Resources Before, Resources After,
        Lane[] Lanes, List<Resources> Samples);
}
