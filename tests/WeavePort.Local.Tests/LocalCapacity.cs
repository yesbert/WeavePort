using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;

internal static class LocalCapacity
{
    private sealed record Tenant(int Index, string Language, IPluginSession Session);
    private sealed record Calls(int Tenant, string Language, double[] LatenciesMs, Dictionary<string, int> Statuses);
    private sealed record Resource(double ElapsedSeconds, long HostRssBytes, long? WorkerRssBytes, double? SystemFreePercent, double? SystemSwapMiB, double HostCpuSeconds, double? WorkerCpuSeconds, int ThreadPoolThreads, long PendingWorkItems, double QueueDelayMs);

    internal static async Task<int> RunAsync(LocalConfiguration config, string output, LinuxComparison? comparison = null)
    {
        if (await LocalVerification.DoctorAsync(config, output) != 0) return 1;
        using var diagnostics = new CapacityDiagnostics();
        double? initialSwapMiB = await CapacityDiagnostics.SwapMiBAsync();
        int[] counts = (Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_COUNTS") ?? "1,64,128,256,384").Split(',').Select(int.Parse).ToArray();
        int[] payloadSizes = (Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_PAYLOAD_CHARS") ?? "16,65536").Split(',').Select(int.Parse).ToArray();
        if (payloadSizes.Length == 0 || payloadSizes.Any(n => n is not (16 or 65536)) || !payloadSizes.SequenceEqual(payloadSizes.Distinct().Order())) throw new ArgumentException("Payloads must be ascending unique values from 16,65536");
        int maxSummedRssGiB = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_MAX_RSS_GIB") ?? "12");
        if (maxSummedRssGiB != 0 && maxSummedRssGiB is < 12 or > 32) throw new ArgumentException("RSS exploration ceiling must be 0 (disabled) or 12..32 GiB");
        int maxHostRssGiB = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_MAX_HOST_RSS_GIB") ?? "2");
        if (maxHostRssGiB is < 0 or > 32) throw new ArgumentException("Host RSS ceiling must be 0 (disabled) or 1..32 GiB");
        int observationBudget = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_OBSERVATION_BUDGET") ?? "8000000");
        if (observationBudget is < 8000000 or > 32000000) throw new ArgumentException("Observation budget must be 8000000..32000000");
        int seconds = int.Parse(Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_SECONDS") ?? "20");
        if (seconds is < 2 or > 60 || counts.Length == 0 || counts.Any(n => n is < 1 or > 4096) || !counts.SequenceEqual(counts.Distinct().Order())) throw new ArgumentException("Invalid bounded local load settings");
        await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new
        {
            counts, seconds, payloadSizes, config.UseUnixSocket, config.SocketBufferBytes, runtime = Environment.Version.ToString(), serverGc = System.Runtime.GCSettings.IsServerGC,
            gc = GC.GetConfigurationVariables(), cores = Environment.ProcessorCount, protection = comparison is null ? "trusted-process-no-sandbox" : "see-comparison.json",
            observationBudget, workerRssGuardActive = comparison is null && maxSummedRssGiB != 0, swapGrowthGuardActive = OperatingSystem.IsMacOS(), hostRssGuardActive = maxHostRssGiB != 0, maxHostRssGiB, maxSummedRssGiB, minimumSystemFreePercent = 15, initialSwapMiB, maxSwapGrowthMiB = 1024,
            resourceScope = comparison is not null ? "Coordinator root RSS/CPU and Linux VM available memory only; worker observations omitted in both modes" : "Sampled root process RSS, summed with shared pages potentially counted repeatedly; descendants not included."
        }, new JsonSerializerOptions { WriteIndented = true }));
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: counts.Max(), MemoryBudgetMiB: counts.Max() * 256, MaximumConcurrentStarts: 8));
        var tenants = new List<Tenant>();
        var summaries = new List<object>();
        string? stop = null;
        try
        {
            foreach (int count in counts)
            {
                long start = Stopwatch.GetTimestamp();
                // Bound starts; local memory is sampled again before each group grows.
                while (tenants.Count < count)
                {
                    if (await CapacityDiagnostics.SwapMiBAsync() - initialSwapMiB > 1024) { stop = "system-swap-growth-during-startup"; break; }
                    if (await SystemFreeAsync() is < 15) { stop = "system-memory-headroom-during-startup"; break; }
                    int first = tenants.Count;
                    Tenant[] group = await Task.WhenAll(Enumerable.Range(first, Math.Min(8, count - first)).Select(async index =>
                    {
                        string language = new[] { "csharp", "python", "typescript" }[index % 3];
                        IPluginSession session = await host.BindAsync(LocalVerification.Context("capacity-" + index), comparison?.Profile(config, language) ?? config.Profile(language, TimeSpan.FromSeconds(10)), new LocalVerification.Callbacks(), []);
                        await LocalVerification.CallAsync(session, "echo", new { tenant = index });
                        return new Tenant(index, language, session);
                    }));
                    tenants.AddRange(group);
                }
                if (stop is not null) break;
                Console.WriteLine($"Started {count} workers; growth seconds={Stopwatch.GetElapsedTime(start).TotalSeconds:F2}");
                foreach (int size in payloadSizes)
                {
                    await StageAsync(size, seconds, false);
                    if (stop is not null) break;
                }
                if (stop is not null) break;
            }
            foreach (Tenant tenant in tenants.Skip(1)) await tenant.Session.DisposeAsync();
            if (tenants.Count > 1) tenants.RemoveRange(1, tenants.Count - 1);
            if (tenants.Count != 0) await StageAsync(16, 5, true);
        }
        catch (Exception error)
        {
            stop = "execution-" + error.GetType().Name;
            Console.Error.WriteLine(stop);
        }
        finally
        {
            try { await host.DisposeAsync(); }
            catch (Exception error)
            {
                stop = "cleanup-" + error.GetType().Name;
                Console.Error.WriteLine(stop);
            }
            await diagnostics.SaveAsync(output);
            await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { stopReason = stop ?? "configured-maximum", stages = summaries, workersAfterCleanup = host.Snapshot.Workers }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return stop is null ? 0 : 1;

        async Task StageAsync(int size, int duration, bool recovery)
        {
            using var observe = new CancellationTokenSource();
            string name = recovery ? "recovery" : tenants.Count + "-" + size;
            var resources = new List<Resource>();
            long started = Stopwatch.GetTimestamp();
            Task sampler = SampleAsync();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int budget = observationBudget / tenants.Count;
            Task<Calls>[] tasks = tenants.Select(async tenant =>
            {
                JsonElement payload = JsonSerializer.SerializeToElement(new { tenant = tenant.Index, text = new string('x', size) });
                var latencies = new List<double>();
                var statuses = new Dictionary<string, int>();
                await ready.Task;
                while (Stopwatch.GetElapsedTime(started).TotalSeconds < duration && (recovery || Volatile.Read(ref stop) is null))
                {
                    if (latencies.Count >= budget) { Interlocked.CompareExchange(ref stop, "observation-budget", null); break; }
                    InvocationResult result = await tenant.Session.InvokeAsync("echo", payload);
                    string status = result.Status;
                    if (status == "ok" && result.Value.GetProperty("tenant").GetInt32() != tenant.Index) status = "wrong-tenant";
                    latencies.Add(result.ElapsedMs);
                    statuses[status] = statuses.GetValueOrDefault(status) + 1;
                }
                return new Calls(tenant.Index, tenant.Language, latencies.ToArray(), statuses);
            }).ToArray();
            using Process coordinator = Process.GetCurrentProcess();
            TimeSpan cpu = coordinator.TotalProcessorTime;
            TimeSpan userCpu = coordinator.UserProcessorTime;
            TimeSpan kernelCpu = coordinator.PrivilegedProcessorTime;
            TimeSpan gc = GC.GetTotalPauseDuration();
            long allocated = GC.GetTotalAllocatedBytes();
            started = Stopwatch.GetTimestamp();
            ready.SetResult();
            Calls[] rows = await Task.WhenAll(tasks);
            double elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
            double hostCpuSeconds = (coordinator.TotalProcessorTime - cpu).TotalSeconds;
            double hostUserCpuSeconds = (coordinator.UserProcessorTime - userCpu).TotalSeconds;
            double hostKernelCpuSeconds = (coordinator.PrivilegedProcessorTime - kernelCpu).TotalSeconds;
            double gcMs = (GC.GetTotalPauseDuration() - gc).TotalMilliseconds;
            long allocationBytes = GC.GetTotalAllocatedBytes() - allocated;
            await observe.CancelAsync();
            await sampler;
            double[] all = rows.SelectMany(r => r.LatenciesMs).Order().ToArray();
            double Percentile(double[] data) => data.Length == 0 ? 0 : data[Math.Clamp((int)Math.Ceiling(data.Length * .99) - 1, 0, data.Length - 1)];
            double p99 = Percentile(all);
            double worst = rows.Max(r => Percentile(r.LatenciesMs.Order().ToArray()));
            int unserved = rows.Count(r => r.LatenciesMs.Length == 0);
            long errors = rows.Sum(r => r.Statuses.Where(p => p.Key != "ok").Sum(p => (long)p.Value));
            double rps = all.Length / elapsed;
            bool completedDuration = elapsed >= duration;
            bool qualified = completedDuration && errors == 0 && unserved == 0 && worst <= (size == 16 ? 500 : 2000);
            if (!qualified) stop ??= recovery ? "recovery-quality" : "customer-quality";
            var languages = rows.GroupBy(r => r.Language).Select(group => new { language = group.Key, customers = group.Count(), requests = group.Sum(r => r.LatenciesMs.Length), p99Ms = Percentile(group.SelectMany(r => r.LatenciesMs).Order().ToArray()), minRequests = group.Min(r => r.LatenciesMs.Length), maxRequests = group.Max(r => r.LatenciesMs.Length) }).ToArray();
            var summary = new { name, tenants = tenants.Count, payloadChars = size, offeredSeconds = duration, elapsedSeconds = elapsed, completedDuration, requests = all.Length, errors, requestsPerSecond = rps, p99Ms = p99, worstTenantP99Ms = worst, unserved, minTenantRequests = rows.Min(r => r.LatenciesMs.Length), maxTenantRequests = rows.Max(r => r.LatenciesMs.Length), qualified, hostCpuSeconds, hostUserCpuSeconds, hostKernelCpuSeconds, languages, gcPauseMs = gcMs, allocationBytes, countersExcludeAnalysis = true };
            summaries.Add(summary);
            await using (var file = File.Create(Path.Combine(output, name + ".json.gz")))
            await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                await JsonSerializer.SerializeAsync(gzip, new { summary, resources, rows });
            Console.WriteLine($"{name}: rps={rps:F1}, p99={p99:F2}ms, worst={worst:F2}ms, errors={errors}, stop={stop ?? "none"}");

            async Task SampleAsync()
            {
                try
                {
                    while (!observe.IsCancellationRequested)
                    {
                        long rss = 0;
                        double workerCpu = 0;
                        foreach (Tenant tenant in comparison is null ? tenants : [])
                        {
                            int pid = int.Parse(tenant.Session.Instance[(tenant.Session.Instance.LastIndexOf("-p", StringComparison.Ordinal) + 2)..]);
                            using Process worker = Process.GetProcessById(pid);
                            rss += worker.WorkingSet64;
                            workerCpu += worker.TotalProcessorTime.TotalSeconds;
                        }
                        using Process self = Process.GetCurrentProcess();
                        double? free = await SystemFreeAsync();
                        double? swap = await CapacityDiagnostics.SwapMiBAsync();
                        long queued = Stopwatch.GetTimestamp();
                        int threads = ThreadPool.ThreadCount;
                        long pending = ThreadPool.PendingWorkItemCount;
                        await Task.Run(static () => { }).WaitAsync(observe.Token);
                        double queueDelay = Stopwatch.GetElapsedTime(queued).TotalMilliseconds;
                        resources.Add(new Resource(Stopwatch.GetElapsedTime(started).TotalSeconds, self.WorkingSet64, comparison is null ? rss : null, free, swap, self.TotalProcessorTime.TotalSeconds, comparison is null ? workerCpu : null, threads, pending, queueDelay));
                        if (!recovery && ((maxHostRssGiB != 0 && self.WorkingSet64 > (long)maxHostRssGiB * 1024 * 1024 * 1024) || (maxSummedRssGiB != 0 && rss + self.WorkingSet64 > (long)maxSummedRssGiB * 1024 * 1024 * 1024) || free < 15 || swap - initialSwapMiB > 1024))
                            Interlocked.CompareExchange(ref stop, (maxHostRssGiB != 0 && self.WorkingSet64 > (long)maxHostRssGiB * 1024 * 1024 * 1024) ? "host-rss-ceiling" : free < 15 ? "system-memory-headroom" : swap - initialSwapMiB > 1024 ? "system-swap-growth" : "summed-rss-exploration-ceiling", null);
                        await Task.Delay(1000, observe.Token);
                    }
                }
                catch (OperationCanceledException) when (observe.IsCancellationRequested) { }
                catch (Exception error) { Interlocked.CompareExchange(ref stop, "resource-observer-" + error.GetType().Name, null); }
            }
        }
    }

    private static async Task<double?> SystemFreeAsync()
    {
        if (OperatingSystem.IsLinux())
        {
            string[] lines = await File.ReadAllLinesAsync("/proc/meminfo");
            long Read(string key) => long.Parse(lines.Single(line => line.StartsWith(key + ":", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            return 100.0 * Read("MemAvailable") / Read("MemTotal");
        }
        if (!OperatingSystem.IsMacOS()) return null;
        string value = await LocalConfiguration.VersionAsync("/usr/bin/memory_pressure", "-Q");
        string last = value.Split('\n').Last(line => line.Contains("System-wide memory free percentage:", StringComparison.Ordinal));
        return double.Parse(last.Split(':')[1].Trim().TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
    }
}
