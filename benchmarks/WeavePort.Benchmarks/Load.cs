using System.Diagnostics;
using System.Text.Json;
using WeavePort.SdkFixture;

namespace WeavePort.Benchmarks;

internal static class Load
{
    internal static async Task MeasureAsync(string language, bool remote, string scenario, int concurrency, int repeat, int durationSeconds = 3)
    {
        if (durationSeconds is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        Console.WriteLine("Creating harness");
        await using var harness = await Harness.CreateAsync(Enumerable.Repeat(language, concurrency).ToArray(), remote);
        Console.WriteLine("Warming bindings");
        for (int i = 0; i < concurrency; i++)
            for (int warm = 0; warm < 2; warm++) Workload.Check(await Workload.ExecuteAsync(harness.Clients[i], scenario), scenario, "tenant-" + i);
        Console.WriteLine("Reading worker identities");
        int[] workers = await harness.WorkerIdsAsync();
        if (workers.Length != concurrency) throw new InvalidDataException("Worker count mismatch.");
        Console.WriteLine("Starting measurement");
        using var stop = new CancellationTokenSource();
        var samples = new List<Resources>();
        var clock = Stopwatch.StartNew();
        Resources before = Resources.Capture(workers, harness.GatewayId, 0);
        samples.Add(before);
        Task sampler = SampleAsync();
        long end = Stopwatch.GetTimestamp() + durationSeconds * Stopwatch.Frequency;
        long allocated = GC.GetTotalAllocatedBytes();
        Task<Lane>[] pending = harness.Clients.Select((client, lane) => Task.Run(async () =>
        {
            var result = new Lane();
            while (Stopwatch.GetTimestamp() < end)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    var output = await Workload.ExecuteAsync(client, scenario);
                    double elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                    Workload.Check(output, scenario, "tenant-" + lane);
                    result.Record(elapsed);
                }
                catch (Exception error) { result.Errors++; result.FirstError = error.GetType().Name + ": " + error.Message; break; }
            }
            return result;
        })).ToArray();
        Console.WriteLine("Awaiting lanes");
        Lane[] lanes = await Task.WhenAll(pending);
        Console.WriteLine("Lanes completed");
        double seconds = clock.Elapsed.TotalSeconds;
        allocated = GC.GetTotalAllocatedBytes() - allocated;
        Resources after = Resources.Capture(workers, harness.GatewayId, seconds);
        Console.WriteLine("Stopping sampler");
        stop.Cancel(); await sampler; samples.Add(after);
        long count = lanes.Sum(l => l.Count);
        long[] histogram = new long[Lane.Buckets];
        foreach (var lane in lanes) for (int i = 0; i < histogram.Length; i++) histogram[i] += lane.Histogram[i];
        var report = new
        {
            language, topology = remote ? "gateway" : "local", scenario, concurrency, repeat, durationSeconds, seconds, count,
            errors = lanes.Sum(l => l.Errors), rps = count / seconds, p50Ms = Lane.Quantile(histogram, .5), p99Ms = Lane.Quantile(histogram, .99),
            allocated, hostCpuSeconds = after.HostCpu - before.HostCpu, gatewayCpuSeconds = after.GatewayCpu - before.GatewayCpu, workerCpuSeconds = after.WorkerCpu - before.WorkerCpu,
            peakHostRss = samples.Max(s => s.HostRss), peakGatewayRss = samples.Max(s => s.GatewayRss), peakWorkerRss = samples.Max(s => s.WorkerRss),
            peakCombinedRss = samples.Max(s => s.HostRss + s.GatewayRss + s.WorkerRss), workers, gateway = harness.GatewayId,
            lanes = lanes.Select(lane => new { lane.Count, lane.Errors, lane.FirstError,
                p50Ms = Lane.Quantile(lane.Histogram, .5), p99Ms = Lane.Quantile(lane.Histogram, .99) }), samples
        };
        Console.WriteLine($"Writing report: lanes={lanes.Length}, samples={samples.Count}, calls={count}, managedBytes={GC.GetTotalMemory(false)}, peakHostRss={samples.Max(s => s.HostRss)}, peakGatewayRss={samples.Max(s => s.GatewayRss)}");
        foreach (var lane in lanes) Console.WriteLine($"Lane: count={lane.Count}, errors={lane.Errors}, errorLength={lane.FirstError?.Length}, error={lane.FirstError?[..Math.Min(160, lane.FirstError.Length)]}");
        await using (var reportFile = File.Create(Path.Combine(Harness.Output, "load.json")))
            await JsonSerializer.SerializeAsync(reportFile, report);
        if (lanes.Any(l => l.Count == 0 || l.Errors > 0)) throw new InvalidOperationException("SDK load failed.");
        async Task SampleAsync()
        {
            try { while (true) { await Task.Delay(100, stop.Token); samples.Add(Resources.Capture(workers, harness.GatewayId, clock.Elapsed.TotalSeconds)); } }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }
}

internal sealed record Resources(double Seconds, long HostRss, long GatewayRss, long WorkerRss, double HostCpu, double GatewayCpu, double WorkerCpu)
{
    internal static Resources Capture(int[] ids, int gateway, double seconds)
    {
        using Process host = Process.GetCurrentProcess();
        long workerRss = 0, gatewayRss = 0;
        double workerCpu = 0, gatewayCpu = 0;
        foreach (int id in ids)
        {
            using Process worker = Process.GetProcessById(id);
            workerRss += worker.WorkingSet64; workerCpu += worker.TotalProcessorTime.TotalSeconds;
        }
        if (gateway != 0)
        {
            using Process process = Process.GetProcessById(gateway);
            gatewayRss = process.WorkingSet64; gatewayCpu = process.TotalProcessorTime.TotalSeconds;
        }
        return new(seconds, host.WorkingSet64, gatewayRss, workerRss, host.TotalProcessorTime.TotalSeconds, gatewayCpu, workerCpu);
    }
}
