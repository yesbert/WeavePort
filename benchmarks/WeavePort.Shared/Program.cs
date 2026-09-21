using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? Environment.CurrentDirectory);
string python = args.ElementAtOrDefault(1) ?? FindExecutable("python3");
string node = args.ElementAtOrDefault(2) ?? FindExecutable("node");
var measurements = new List<object>();
string csharp = Environment.GetEnvironmentVariable("WP_SHARED_CSHARP_FIXTURE") ?? Path.Combine(root, "tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll");
string hostBinary = typeof(PluginHost).Assembly.Location;
string hostBinarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hostBinary)));
foreach (var (language, executable, fixture) in new[] { ("python", python, "shared.py"), ("typescript", node, "shared.mjs"), ("csharp", FindExecutable("dotnet"), csharp) })
{
    foreach (string workload in new[] { "delay", "cpu" })
    {
        foreach (int degree in new[] { 1, 16 })
        {
            measurements.Add(await MeasureAsync(language, executable, language == "csharp" ? fixture : Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures", fixture), workload, degree));
        }
    }
}
string path = Path.Combine(root, "reports/benchmarks", $"shared-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
var report = new
{
    recordedUtc = DateTimeOffset.UtcNow,
    environment = new { os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), framework = RuntimeInformation.FrameworkDescription, processors = Environment.ProcessorCount, serverGc = System.Runtime.GCSettings.IsServerGC },
    qualification = Environment.GetEnvironmentVariable("WP_BENCHMARK_QUALIFICATION") ?? "source-build; not a packed release qualification",
    description = "One resident worker; 64 distinct tenants; degree 1 versus 16. Delay is controlled asynchronous I/O latency, not CPU speed. PBKDF2 is native CPU work; Node uses its configured libuv pool. No speed ratio is a correctness gate.",
    sourceHashes = Directory.EnumerateFiles(Path.Combine(root, "src/WeavePort.Hosting"), "*.cs").Concat(Directory.EnumerateFiles(Path.Combine(root, "sdks/python/weaveport_sdk"), "*.py")).Concat(Directory.EnumerateFiles(Path.Combine(root, "sdks/typescript/src"), "*.ts")).ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))),
    hostBinary, hostBinarySha256,
    measurements
};
await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(path);

static string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory, name)).First(File.Exists);
static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
static async Task<object> MeasureAsync(string language, string executable, string fixture, string workload, int degree)
{
    await using var host = new PluginHost(new SchedulingOptions { MaximumWorkers = 2, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512, QueueTimeout = TimeSpan.FromSeconds(60), NormalTimeout = TimeSpan.FromSeconds(30) });
    var context = new PluginContext("operator", "benchmark", "1", "shared", Json(new { }));
    List<string> launch = language == "csharp" ? [fixture, "--host-shared"] : [fixture];
    string? sdkPath = language == "csharp" ? null : Environment.GetEnvironmentVariable(language == "python" ? "WP_SHARED_PYTHON_SDK" : "WP_SHARED_NODE_SDK");
    if (sdkPath is not null) launch.AddRange(["--sdk-path", sdkPath]);
    var profile = new ProcessProfile(executable, launch, trustedCode: true, reservedMemoryMiB: 128, timeout: TimeSpan.FromSeconds(30)) { ReusePolicy = WorkerReusePolicy.Shared };
    await using var shared = await host.ShareAsync(context, profile, new() { Degree = degree }, new NoCallbacks(), []);
    await using var probe = shared.For("probe");
    int pid = (await probe.CallAsync("echo", Json(new { }))).GetProperty("pid").GetInt32();
    using var process = Process.GetProcessById(pid);
    JsonElement input = workload == "delay" ? Json(new { milliseconds = 150 }) : Json(new { iterations = 100000 });
    await probe.CallAsync(workload, input);
    string expectedDigest = Convert.ToHexStringLower(Rfc2898DeriveBytes.Pbkdf2("weaveport"u8, "benchmark"u8, 100000, HashAlgorithmName.SHA256, 32));
    const int count = 64;
    var clients = Enumerable.Range(0, count).Select(i => shared.For($"tenant-{i}")).ToArray();
    var latencies = new double[count];
    long peakRss = 0;
    using var sampling = new CancellationTokenSource();
    Task sampler = Task.Run(async () =>
    {
        while (!sampling.IsCancellationRequested)
        {
            process.Refresh();
            peakRss = Math.Max(peakRss, process.WorkingSet64);
            try { await Task.Delay(10, sampling.Token); } catch (OperationCanceledException) { }
        }
    });
    long allocated = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    await Task.WhenAll(clients.Select(async (client, i) =>
    {
        long started = Stopwatch.GetTimestamp();
        JsonElement result = await client.CallAsync(workload, input);
        string? tenant = workload == "delay" ? result.GetString() : result.GetProperty("tenant").GetString();
        if (tenant != $"tenant-{i}") throw new InvalidDataException("Tenant mismatch in benchmark.");
        if (workload == "cpu" && result.GetProperty("digest").GetString() != expectedDigest) throw new InvalidDataException("CPU result integrity failure.");
        latencies[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }));
    watch.Stop();
    allocated = GC.GetTotalAllocatedBytes(true) - allocated;
    sampling.Cancel();
    await sampler;
    foreach (var client in clients) await client.DisposeAsync();
    Array.Sort(latencies);
    double throughput = count / watch.Elapsed.TotalSeconds;
    Console.WriteLine($"{language,-10} {workload,-5} degree={degree,2}: {throughput:F1} calls/s p95={latencies[(int)Math.Ceiling(count * .95) - 1]:F1}ms RSS={peakRss / 1048576d:F1}MiB");
    return new { language, workload, degree, count, elapsedMs = watch.Elapsed.TotalMilliseconds, throughputPerSecond = throughput, p50Ms = latencies[count / 2], p95Ms = latencies[(int)Math.Ceiling(count * .95) - 1], p99Ms = latencies[(int)Math.Ceiling(count * .99) - 1], sampledWorkerPeakRssBytes = peakRss, coordinatorAllocatedBytes = allocated, workerCount = shared.Snapshot.ReadyWorkers, correctness = "all tenant identities matched; CPU digests verified; all calls completed" };
}
sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new InvalidOperationException("No benchmark callbacks.");
}
