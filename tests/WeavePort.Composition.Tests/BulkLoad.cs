using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

/// <summary>Runs serial/fan-out sizes against each independently owned tenant population.</summary>
internal sealed class BulkLoad(string config, string output)
{
    private readonly List<object> _rows = [];

    internal async Task RunAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new
        {
            runtime = Environment.Version.ToString(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            cores = Environment.ProcessorCount,
            serverGc = System.Runtime.GCSettings.IsServerGC,
            gc = GC.GetConfigurationVariables(),
            workerScope = "Native root RSS with repeated shared pages; no descendants or OS sandbox",
            caller = "Full byte verification; no network HTTP client",
            protection = "trusted-native-process"
        }, new JsonSerializerOptions { WriteIndented = true }));
        int[] sizes = (Environment.GetEnvironmentVariable("WEAVEPORT_BULK_SIZES_KIB") ?? "256,1024,8192,32768,131072").Split(',').Select(int.Parse).ToArray();
        int[] customers = (Environment.GetEnvironmentVariable("WEAVEPORT_BULK_CUSTOMERS") ?? "1,4").Split(',').Select(int.Parse).ToArray();
        int[] chunks = (Environment.GetEnvironmentVariable("WEAVEPORT_BULK_CHUNKS_KIB") ?? "64").Split(',').Select(int.Parse).ToArray();
        if (sizes.Any(n => n is < 256 or > 131072 || n % 64 != 0) || customers.Any(n => n is < 1 or > 16) || chunks.Any(n => n is < 4 or > 256))
        {
            throw new ArgumentException("Invalid bounded bulk load settings.");
        }

        foreach (int count in customers)
        {
            await RunPopulationAsync(count, sizes, chunks);
        }
        if (Directory.Exists(Path.Combine(output, "store")) && Directory.EnumerateFileSystemEntries(Path.Combine(output, "store")).Any())
        {
            throw new IOException("Result cleanup incomplete.");
        }

        await File.WriteAllTextAsync(Path.Combine(output, "completion.json"), JsonSerializer.Serialize(new
        {
            completedCases = _rows.Count,
            errors = 0,
            resultFilesAfterCleanup = 0
        }));

    }

    private async Task RunPopulationAsync(int count, int[] sizes, int[] chunks)
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 256, MemoryBudgetMiB: 65536, MaximumConcurrentStarts: 16));
        var scenarios = new List<BulkScenario>();
        try
        {
            for (int index = 0; index < count; index++)
            {
                scenarios.Add(await BulkScenario.CreateAsync(config, Path.Combine(output, "store"), "tenant-" + index, host));
            }

            var cases = from kib in sizes
                        from chunk in chunks
                        from fan in new[] { false, true }
                        select (kib, chunk, fan);
            foreach (var (kib, chunk, fan) in cases)
            {
                await MeasureAsync(scenarios, kib, chunk, fan);
            }
        }
        finally
        {
            foreach (var scenario in scenarios)
            {
                await scenario.DisposeAsync();
            }
        }
    }

    private async Task MeasureAsync(List<BulkScenario> scenarios, int kib, int chunk, bool fan)
    {
        int count = scenarios.Count;
        var samples = new List<object>();
        using var observe = new CancellationTokenSource();
        Task sampler = Task.Run(() => SampleResourcesAsync(scenarios, samples, observe.Token));
        long allocated = GC.GetTotalAllocatedBytes(true);
        long start = Stopwatch.GetTimestamp();
        double[] elapsed = new double[count];
        double[] requestsMs = new double[count * 3];
        long[] delivered;
        try
        {
            delivered = await Task.WhenAll(scenarios.Select(async (s, index) =>
            {
                long began = Stopwatch.GetTimestamp();
                long total = 0;
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    long callStart = Stopwatch.GetTimestamp();
                    total += await s.RunAsync(checked(kib * 1024), fan, checked(chunk * 1024));
                    requestsMs[index * 3 + iteration] = Stopwatch.GetElapsedTime(callStart).TotalMilliseconds;
                }
                elapsed[index] = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                return total;
            }));
        }
        finally
        {
            observe.Cancel();
            await sampler;
        }
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        _rows.Add(new
        {
            customers = count,
            inputKiB = kib,
            chunkKiB = chunk,
            topology = fan ? "fan-out-3-concatenate" : "serial-3",
            repetitions = 3,
            seconds,
            deliveredBytes = delivered.Sum(),
            deliveredMiBPerSecond = delivered.Sum() / 1048576.0 / seconds,
            perCustomerThreeRequestsMs = elapsed,
            requestsMs,
            hostAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
            samples,
            correctness = "all caller bytes checked",
            errors = 0
        });
        await File.WriteAllTextAsync(Path.Combine(output, "load.json"), JsonSerializer.Serialize(_rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{count} customers {kib} KiB {chunk} KiB chunks fan={fan}: {seconds:F2}s {delivered.Sum() / 1048576.0 / seconds:F1} delivered MiB/s");
    }
    private static async Task SampleResourcesAsync(List<BulkScenario> scenarios, List<object> samples, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var host = Process.GetCurrentProcess();
                var workers = scenarios.Select(s => s.WorkerResources()).ToArray();
                samples.Add(new
                {
                    at = Stopwatch.GetTimestamp(),
                    hostRssBytes = host.WorkingSet64,
                    workerRootRssBytes = workers.Sum(w => w.Rss),
                    workerRootCpuSeconds = workers.Sum(w => w.Cpu),
                    managedBytes = GC.GetTotalMemory(false),
                    cpuSeconds = host.TotalProcessorTime.TotalSeconds
                });
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
