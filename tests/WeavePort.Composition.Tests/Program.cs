using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

string config = Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_CONFIG") ?? throw new ArgumentException("Set WEAVEPORT_LOCAL_CONFIG to an absolute fixture config path.");
string output = Environment.GetEnvironmentVariable("WEAVEPORT_BULK_OUTPUT") ?? throw new ArgumentException("Set WEAVEPORT_BULK_OUTPUT to a new evidence directory.");
Directory.CreateDirectory(output);
if (args.FirstOrDefault() == "http") { await HttpChecks.RunAsync(config, output); return; }
if (args.FirstOrDefault() == "verify") { await BulkChecks.RunAsync(config, output); return; }
if (args.FirstOrDefault() != "load") throw new ArgumentException("Expected verify, http or load.");
await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, cores = Environment.ProcessorCount, serverGc = System.Runtime.GCSettings.IsServerGC, gc = GC.GetConfigurationVariables(), workerScope = "Native root RSS with repeated shared pages; no descendants or OS sandbox", caller = "Full byte verification; no network HTTP client", protection = "trusted-native-process" }, new JsonSerializerOptions { WriteIndented = true }));
var rows = new List<object>();
int[] sizes = (Environment.GetEnvironmentVariable("WEAVEPORT_BULK_SIZES_KIB") ?? "256,1024,8192,32768,131072").Split(',').Select(int.Parse).ToArray();
int[] customers = (Environment.GetEnvironmentVariable("WEAVEPORT_BULK_CUSTOMERS") ?? "1,4").Split(',').Select(int.Parse).ToArray();
int[] chunks = (Environment.GetEnvironmentVariable("WEAVEPORT_BULK_CHUNKS_KIB") ?? "64").Split(',').Select(int.Parse).ToArray();
if (sizes.Any(n => n is < 256 or > 131072 || n % 64 != 0) || customers.Any(n => n is < 1 or > 16) || chunks.Any(n => n is < 4 or > 256)) throw new ArgumentException("Invalid bounded bulk load settings.");
foreach (int count in customers)
{
    await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 256, MemoryBudgetMiB: 65536, MaximumConcurrentStarts: 16));
    var scenarios = new List<BulkScenario>();
    try
    {
        for (int i = 0; i < count; i++) scenarios.Add(await BulkScenario.CreateAsync(config, Path.Combine(output, "store"), "tenant-" + i, host));
        foreach (int kib in sizes) foreach (int chunk in chunks) foreach (bool fan in new[] { false, true })
        {
            var samples = new List<object>();
            using var observe = new CancellationTokenSource();
            Task sampler = Task.Run(async () =>
            {
                try
                {
                    while (!observe.IsCancellationRequested)
                    {
                        using var host = Process.GetCurrentProcess();
                        var workers = scenarios.Select(s => s.WorkerResources()).ToArray();
                        samples.Add(new { at = Stopwatch.GetTimestamp(), hostRssBytes = host.WorkingSet64, workerRootRssBytes = workers.Sum(w => w.Rss), workerRootCpuSeconds = workers.Sum(w => w.Cpu), managedBytes = GC.GetTotalMemory(false), cpuSeconds = host.TotalProcessorTime.TotalSeconds });
                        await Task.Delay(100, observe.Token);
                    }
                }
                catch (OperationCanceledException) when (observe.IsCancellationRequested) { }
            });
            long allocated = GC.GetTotalAllocatedBytes(true); long start = Stopwatch.GetTimestamp();
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
                    elapsed[index] = Stopwatch.GetElapsedTime(began).TotalMilliseconds; return total;
                }));
            }
            finally { observe.Cancel(); await sampler; }
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
            rows.Add(new { customers = count, inputKiB = kib, chunkKiB = chunk, topology = fan ? "fan-out-3-concatenate" : "serial-3", repetitions = 3, seconds, deliveredBytes = delivered.Sum(), deliveredMiBPerSecond = delivered.Sum() / 1048576.0 / seconds, perCustomerThreeRequestsMs = elapsed, requestsMs, hostAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, samples, correctness = "all caller bytes checked", errors = 0 });
            await File.WriteAllTextAsync(Path.Combine(output, "load.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"{count} customers {kib} KiB {chunk} KiB chunks fan={fan}: {seconds:F2}s {delivered.Sum()/1048576.0/seconds:F1} delivered MiB/s");
        }
    }
    finally { foreach (var scenario in scenarios) await scenario.DisposeAsync(); }
}
if (Directory.Exists(Path.Combine(output, "store")) && Directory.EnumerateFileSystemEntries(Path.Combine(output, "store")).Any()) throw new IOException("Result cleanup incomplete.");

await File.WriteAllTextAsync(Path.Combine(output, "completion.json"), JsonSerializer.Serialize(new { completedCases = rows.Count, errors = 0, resultFilesAfterCleanup = 0 }));
