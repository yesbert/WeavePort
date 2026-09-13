using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeavePort.CapacityTests;

internal sealed record WorkerSample(DateTimeOffset Utc, string Name, double MemoryBytes, double CpuPercent, int Pids);
internal sealed record ResourceSample(DateTimeOffset Utc, string Phase, VmSample Vm, long HostRssBytes, long ManagedBytes,
    double HostCpuPercent, long ChildRssBytes, int ChildCount, long AppleVmProcessesRssBytes, double AppleVmProcessesCpuPercent,
    double? MacMemoryFreePercent, string? MacSwapUsage, int ThreadPoolThreads, long QueuedWorkItems,
    int Gen0Collections, int Gen1Collections, int Gen2Collections, double GcPauseMs, long AllocatedBytes, long SkippedStatsLines, int ExpectedWorkers, int MissingWorkers, WorkerSample[] Workers,
    long? CoordinatorCgroupMemoryBytes = null, string? CoordinatorMemoryEvents = null);

internal sealed class Telemetry(VmObserver vm, TimeProvider clock) : IAsyncDisposable
{
    internal static readonly bool WorkerStatsEnabled = Environment.GetEnvironmentVariable("WEAVEPORT_DIAGNOSTICS_NO_WORKER_STATS") != "1";
    private readonly ConcurrentDictionary<string, WorkerSample> _workers = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Process _self = Process.GetCurrentProcess();
    private Process? _stats;
    private Task? _reader;
    private Task<string>? _errors;
    private Task? _sampling;
    private TimeSpan _previousCpu;
    private long _previousTime;
    private string _phase = "baseline";
    private string? _stopReason;
    private bool _changingStats;
    private long _unavailableReadings;
    private Func<string[]> _instances = () => [];
    private HashSet<string> _allowed = [];
    private DateTimeOffset? _missingSince;
    internal string? StopReason => _stopReason;
    internal List<ResourceSample> Samples { get; } = [];
    internal void Phase(string phase) => _phase = phase;

    internal void Start() => _sampling = SampleLoopAsync();

    internal async Task WatchAsync(Func<string[]> getInstances)
    {
        _changingStats = true;
        await StopStatsAsync();
        _workers.Clear();
        _instances = getInstances;
        string[] instances = getInstances();
        _allowed = instances.ToHashSet(StringComparer.Ordinal);
        _missingSince = null;
        if (!WorkerStatsEnabled && !RequestDiagnostics.Enabled) throw new InvalidOperationException("Disabling worker telemetry requires explicit diagnostic mode");
        if (!WorkerStatsEnabled || instances.Length == 0) { _changingStats = false; return; }
        _stats = Commands.Start("docker", ["stats", "--no-trunc", "--format", "{{json .}}"]);
        _errors = _stats.StandardError.ReadToEndAsync();
        _reader = ReadStatsAsync();
        bool CompleteFreshCoverage() => instances.All(name => _workers.TryGetValue(name, out WorkerSample? sample) && (clock.GetUtcNow() - sample.Utc).TotalSeconds <= 10);
        for (int n = 0; n < 600 && !CompleteFreshCoverage() && !_reader.IsCompleted; n++) await Task.Delay(100);
        if (!CompleteFreshCoverage())
        {
            string[] missing = instances.Where(name => !_workers.TryGetValue(name, out WorkerSample? sample) || (clock.GetUtcNow() - sample.Utc).TotalSeconds > 10).ToArray();
            if (missing.Length == 0) { _changingStats = false; return; }
            string diagnostics = await Commands.RunAsync("docker", ["inspect", "--format", "{{.Name}} {{json .State}}", .. missing]);
            throw new IOException("Incomplete fresh worker telemetry: " + (instances.Length - missing.Length) + "/" + instances.Length + "; " + diagnostics);
        }
        _changingStats = false;
    }

    private async Task ReadStatsAsync()
    {
        while (await _stats!.StandardOutput.ReadLineAsync() is { } line)
        {
            WorkerSample? sample = ParseSample(line, clock);
            if (sample is null) { Interlocked.Increment(ref _unavailableReadings); continue; }
            if (_allowed.Contains(sample.Name)) _workers[sample.Name] = sample;
        }
    }

    internal static WorkerSample? ParseSample(string line, TimeProvider clock)
    {
        string json = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
        if (string.IsNullOrWhiteSpace(json)) return null;
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement item = document.RootElement;
        string name = item.GetProperty("Name").GetString()!;
        if (name == "--" || new[] { "MemUsage", "CPUPerc", "PIDs" }.Any(key => item.GetProperty(key).GetString()!.Contains("--", StringComparison.Ordinal))) return null;
        return new WorkerSample(clock.GetUtcNow(), name, ParseBytes(item.GetProperty("MemUsage").GetString()!.Split('/')[0].Trim()),
            double.Parse(item.GetProperty("CPUPerc").GetString()!.TrimEnd('%'), CultureInfo.InvariantCulture),
            int.Parse(item.GetProperty("PIDs").GetString()!, CultureInfo.InvariantCulture));
    }

    internal static double ParseBytes(string text)
    {
        foreach ((string suffix, double factor) in new[] { ("GiB", Math.Pow(1024, 3)), ("MiB", Math.Pow(1024, 2)), ("KiB", 1024d), ("GB", 1e9), ("MB", 1e6), ("kB", 1e3), ("B", 1d) })
            if (text.EndsWith(suffix, StringComparison.Ordinal)) return double.Parse(text[..^suffix.Length], CultureInfo.InvariantCulture) * factor;
        throw new FormatException("Unknown memory unit: " + text);
    }

    private async Task SampleLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await SampleAsync();
                await Task.Delay(1000, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) { _stopReason = "telemetry-failure: " + error.GetType().Name + ": " + error.Message; }
    }

    private async Task SampleAsync()
    {
        _self.Refresh();
        long now = clock.GetTimestamp();
        double seconds = _previousTime == 0 ? 1 : clock.GetElapsedTime(_previousTime, now).TotalSeconds;
        double cpu = _previousTime == 0 ? 0 : (_self.TotalProcessorTime - _previousCpu).TotalSeconds / seconds * 100;
        _previousCpu = _self.TotalProcessorTime;
        _previousTime = now;
        long children = 0, vmRss = 0;
        int childCount = 0;
        double vmCpu = 0;
        string processes = await Commands.RunAsync("ps", "-axo", "pid=,ppid=,rss=,%cpu=,comm=");
        foreach (string line in processes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5) continue;
            if (int.Parse(fields[1], CultureInfo.InvariantCulture) == Environment.ProcessId)
            {
                children += long.Parse(fields[2], CultureInfo.InvariantCulture) * 1024;
                childCount++;
            }
            if (line.Contains("com.apple.Virtualization.VirtualMachine", StringComparison.Ordinal))
            {
                vmRss += long.Parse(fields[2], CultureInfo.InvariantCulture) * 1024;
                vmCpu += double.Parse(fields[3], CultureInfo.InvariantCulture);
            }
        }
        DateTimeOffset utc = clock.GetUtcNow();
        VmSample machine = vm.Latest;
        _allowed = _instances().ToHashSet(StringComparer.Ordinal);
        WorkerSample[] workers = _workers.Values.Where(w => _allowed.Contains(w.Name)).ToArray();
        int missing = _allowed.Count - workers.Length;
        _missingSince = missing == 0 ? null : _missingSince ?? utc;
        double? macFree = null;
        string? swap = null;
        if (OperatingSystem.IsMacOS())
        {
            string pressure = await Commands.RunAsync("memory_pressure", "-Q");
            swap = (await Commands.RunAsync("sysctl", "-n", "vm.swapusage")).Trim();
            Match free = Regex.Match(pressure, @"System-wide memory free percentage: (\d+)%");
            if (!free.Success) throw new IOException("macOS memory telemetry unavailable");
            macFree = double.Parse(free.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        long? cgroupMemory = null;
        string? memoryEvents = null;
        if (OperatingSystem.IsLinux() && File.Exists("/sys/fs/cgroup/memory.current"))
        {
            cgroupMemory = long.Parse(await File.ReadAllTextAsync("/sys/fs/cgroup/memory.current"), CultureInfo.InvariantCulture);
            memoryEvents = await File.ReadAllTextAsync("/sys/fs/cgroup/memory.events");
        }
        Samples.Add(new ResourceSample(utc, _phase, machine, _self.WorkingSet64, GC.GetTotalMemory(false), cpu, children, childCount, vmRss, vmCpu, macFree, swap,
            ThreadPool.ThreadCount, ThreadPool.PendingWorkItemCount, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration().TotalMilliseconds, GC.GetTotalAllocatedBytes(),
            Interlocked.Read(ref _unavailableReadings), _allowed.Count, missing, workers, cgroupMemory, memoryEvents));
        if (!_changingStats && _stats is not null && _reader?.IsCompleted == true) _stopReason = "worker telemetry stream stopped";
        if ((utc - machine.Utc).TotalSeconds > 10) _stopReason = "VM telemetry stale";
        if (WorkerStatsEnabled && !_phase.EndsWith(":startup", StringComparison.Ordinal) && _missingSince is { } since && (utc - since).TotalSeconds > 10) _stopReason = "worker telemetry missing after restart";
        if (!_changingStats && workers.Any(w => (utc - w.Utc).TotalSeconds > 10)) _stopReason = "worker telemetry stale";
        if (machine.AvailableBytes < machine.TotalBytes * 0.2) _stopReason = "VM available memory below 20%";
        if (macFree < 20) _stopReason = "macOS memory free percentage below 20%";
        if (_self.WorkingSet64 > 4L * 1024 * 1024 * 1024) _stopReason = "host working set above 4 GiB";
    }

    private async Task StopStatsAsync()
    {
        if (_stats is null) return;
        if (!_stats.HasExited) _stats.Kill(true);
        await _stats.WaitForExitAsync();
        if (_reader is not null)
        {
            try { await _reader; }
            catch (Exception error) { _stopReason = "worker telemetry-failure: " + error.GetType().Name; }
        }
        if (_errors is not null) await _errors;
        _stats.Dispose();
        _stats = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_sampling is not null) await _sampling;
        await StopStatsAsync();
        _self.Dispose();
        _stop.Dispose();
    }
}
