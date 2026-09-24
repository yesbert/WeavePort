using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;

internal sealed partial class LocalCapacityRun(LocalConfiguration config, string output,
    LinuxComparison? comparison, LocalCapacitySettings settings, double? initialSwapMiB)
{
    private sealed record Tenant(int Index, string Language, IPluginSession Session);
    private sealed record Calls(int Tenant, string Language, double[] LatenciesMs, Dictionary<string, int> Statuses);
    private sealed record Resource(double ElapsedSeconds, long HostRssBytes, long? WorkerRssBytes, double? SystemFreePercent, double? SystemSwapMiB, double HostCpuSeconds, double? WorkerCpuSeconds, int ThreadPoolThreads, long PendingWorkItems, double QueueDelayMs);

    private readonly List<Tenant> _tenants = [];
    private readonly List<object> _summaries = [];
    private string? _stop;
    private PluginHost _host = null!;
    private LinuxComparison? Comparison => comparison;
    private double? InitialSwapMiB => initialSwapMiB;
    private string Output => output;
    private LocalCapacitySettings Settings => settings;
    internal async Task<int> RunAsync()
    {
        using var diagnostics = new CapacityDiagnostics();
        await WriteConfigurationAsync();
        await using var ownedHost = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: settings.Counts.Max(), MemoryBudgetMiB: settings.Counts.Max() * 256, MaximumPristineWorkers: 0, MaximumConcurrentStarts: 8));
        _host = ownedHost;
        try
        {
            foreach (int count in settings.Counts)
            {
                await RunLevelAsync(count);
                if (_stop is not null)
                {
                    break;
                }
            }
            foreach (Tenant tenant in _tenants.Skip(1))
            {
                await tenant.Session.DisposeAsync();
            }

            if (_tenants.Count > 1)
            {
                _tenants.RemoveRange(1, _tenants.Count - 1);
            }

            if (_tenants.Count != 0)
            {
                await StageAsync(16, 5, true);
            }
        }
        catch (Exception error)
        {
            _stop = "execution-" + error.GetType().Name;
            Console.Error.WriteLine(_stop);
        }
        finally
        {
            try
            {
                await _host.DisposeAsync();
            }
            catch (Exception error)
            {
                _stop = "cleanup-" + error.GetType().Name;
                Console.Error.WriteLine(_stop);
            }
            await diagnostics.SaveAsync(output);
            await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                stopReason = _stop ?? "configured-maximum",
                stages = _summaries,
                workersAfterCleanup = _host.Snapshot.Workers
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return _stop is null ? 0 : 1;
    }
    private async Task RunLevelAsync(int count)
    {
        long start = Stopwatch.GetTimestamp();
        // Bound starts; local memory is sampled again before each group grows.
        await GrowAsync(count);
        if (_stop is not null)
        {
            return;
        }

        Console.WriteLine($"Started {count} workers; growth seconds={Stopwatch.GetElapsedTime(start).TotalSeconds:F2}");
        foreach (int size in settings.PayloadSizes)
        {
            await StageAsync(size, settings.Seconds, false);
            if (_stop is not null)
            {
                return;
            }
        }
    }
    private async Task GrowAsync(int count)
    {
        while (_tenants.Count < count)
        {
            if (await CapacityDiagnostics.SwapMiBAsync() - initialSwapMiB > 1024)
            {
                _stop = "system-swap-growth-during-startup";
                break;
            }
            if (await SystemFreeAsync() is < 15)
            {
                _stop = "system-memory-headroom-during-startup";
                break;
            }
            int first = _tenants.Count;
            Tenant[] group = await Task.WhenAll(Enumerable.Range(first, Math.Min(8, count - first)).Select(StartTenantAsync));
            _tenants.AddRange(group);
        }
    }
    private async Task<Tenant> StartTenantAsync(int index)
    {
        string language = new[] { "csharp", "python", "typescript" }[index % 3];
        IPluginSession session = await _host.BindAsync(LocalVerification.Context("capacity-" + index), comparison?.Profile(config, language) ?? config.Profile(language, TimeSpan.FromSeconds(10)), new LocalVerification.Callbacks(), []);
        await LocalVerification.CallAsync(session, "echo", new
        {
            tenant = index
        });
        return new Tenant(index, language, session);
    }
    private Task StageAsync(int size, int duration, bool recovery) => new Stage(this, size, duration, recovery).RunAsync();
    private async Task WriteConfigurationAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new
        {
            counts = settings.Counts,
            seconds = settings.Seconds,
            payloadSizes = settings.PayloadSizes,
            config.UseUnixSocket,
            config.SocketBufferBytes,
            runtime = Environment.Version.ToString(),
            serverGc = System.Runtime.GCSettings.IsServerGC,
            gc = GC.GetConfigurationVariables(),
            cores = Environment.ProcessorCount,
            protection = comparison is null ? "trusted-process-no-sandbox" : "see-comparison.json",
            observationBudget = settings.ObservationBudget,
            workerRssGuardActive = comparison is null && settings.MaximumSummedRssGiB != 0,
            swapGrowthGuardActive = OperatingSystem.IsMacOS(),
            hostRssGuardActive = settings.MaximumHostRssGiB != 0,
            maxHostRssGiB = settings.MaximumHostRssGiB,
            maxSummedRssGiB = settings.MaximumSummedRssGiB,
            minimumSystemFreePercent = 15,
            initialSwapMiB,
            maxSwapGrowthMiB = 1024,
            resourceScope = comparison is not null ? "Coordinator root RSS/CPU and Linux VM available memory only; worker observations omitted in both modes" : "Sampled root process RSS, summed with shared pages potentially counted repeatedly; descendants not included."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task<double?> SystemFreeAsync()
    {
        if (OperatingSystem.IsLinux())
        {
            string[] lines = await File.ReadAllLinesAsync("/proc/meminfo");
            long Read(string key) => long.Parse(lines.Single(line => line.StartsWith(key + ":", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            return 100.0 * Read("MemAvailable") / Read("MemTotal");
        }
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        string value = await LocalConfiguration.VersionAsync("/usr/bin/memory_pressure", "-Q");
        string last = value.Split('\n').Last(line => line.Contains("System-wide memory free percentage:", StringComparison.Ordinal));
        return double.Parse(last.Split(':')[1].Trim().TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
    }
}
