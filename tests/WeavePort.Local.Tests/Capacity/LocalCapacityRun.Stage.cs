using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;

internal sealed partial class LocalCapacityRun
{
    private sealed partial class Stage(LocalCapacityRun owner, int size, int duration, bool recovery)
    {
        private readonly CancellationTokenSource _observe = new();
        private readonly List<Resource> _resources = [];
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _budget = owner.Settings.ObservationBudget / owner._tenants.Count;
        private long _started;
        private sealed record Counters(double Elapsed, double HostCpuSeconds, double HostUserCpuSeconds, double HostKernelCpuSeconds, double GcMs, long AllocationBytes);
        internal async Task RunAsync()
        {
            using var observeLifetime = _observe;
            _started = Stopwatch.GetTimestamp();
            Task sampler = SampleAsync();
            Calls[] rows;
            Counters counters;
            try
            {
                (rows, counters) = await MeasureAsync();
            }
            finally
            {
                await _observe.CancelAsync();
                await sampler;
            }
            await WriteResultsAsync(rows, counters);
        }

        private async Task<(Calls[] Rows, Counters Counters)> MeasureAsync()
        {
            Task<Calls>[] tasks = owner._tenants.Select(RunTenantAsync).ToArray();

            using Process coordinator = Process.GetCurrentProcess();
            TimeSpan cpu = coordinator.TotalProcessorTime;
            TimeSpan userCpu = coordinator.UserProcessorTime;
            TimeSpan kernelCpu = coordinator.PrivilegedProcessorTime;
            TimeSpan gc = GC.GetTotalPauseDuration();
            long allocated = GC.GetTotalAllocatedBytes();
            _started = Stopwatch.GetTimestamp();
            _ready.SetResult();
            Calls[] rows = await Task.WhenAll(tasks);
            double elapsed = Stopwatch.GetElapsedTime(_started).TotalSeconds;
            double hostCpuSeconds = (coordinator.TotalProcessorTime - cpu).TotalSeconds;
            double hostUserCpuSeconds = (coordinator.UserProcessorTime - userCpu).TotalSeconds;
            double hostKernelCpuSeconds = (coordinator.PrivilegedProcessorTime - kernelCpu).TotalSeconds;
            double gcMs = (GC.GetTotalPauseDuration() - gc).TotalMilliseconds;
            long allocationBytes = GC.GetTotalAllocatedBytes() - allocated;
            return (rows, new(elapsed, hostCpuSeconds, hostUserCpuSeconds, hostKernelCpuSeconds, gcMs, allocationBytes));
        }
        private async Task<Calls> RunTenantAsync(Tenant tenant)
        {
            JsonElement payload = JsonSerializer.SerializeToElement(new
            {
                tenant = tenant.Index,
                text = new string('x', size)
            });
            var latencies = new List<double>();
            var statuses = new Dictionary<string, int>();
            await _ready.Task;
            while (Stopwatch.GetElapsedTime(_started).TotalSeconds < duration && (recovery || Volatile.Read(ref owner._stop) is null))
            {
                if (latencies.Count >= _budget)
                {
                    Interlocked.CompareExchange(ref owner._stop, "observation-budget", null);
                    break;
                }
                InvocationResult result = await tenant.Session.InvokeAsync("echo", payload);
                string status = ValidateTenant(result, tenant.Index);

                latencies.Add(result.ElapsedMs);
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
            }
            return new Calls(tenant.Index, tenant.Language, latencies.ToArray(), statuses);
        }
        private static string ValidateTenant(InvocationResult result, int tenant)
        {
            if (result.Status == "ok" && result.Value.GetProperty("tenant").GetInt32() != tenant)
            {
                return "wrong-tenant";
            }
            return result.Status;
        }

        private async Task WriteResultsAsync(Calls[] rows, Counters counters)
        {
            var (elapsed, hostCpuSeconds, hostUserCpuSeconds, hostKernelCpuSeconds, gcMs, allocationBytes) = counters;
            string name = recovery ? "recovery" : owner._tenants.Count + "-" + size;
            double[] all = rows.SelectMany(r => r.LatenciesMs).Order().ToArray();
            double p99 = Percentile(all);
            double worst = rows.Max(r => Percentile(r.LatenciesMs.Order().ToArray()));
            int unserved = rows.Count(r => r.LatenciesMs.Length == 0);
            long errors = rows.Sum(r => r.Statuses.Where(p => p.Key != "ok").Sum(p => (long)p.Value));
            double rps = all.Length / elapsed;
            bool completedDuration = elapsed >= duration;
            bool qualified = completedDuration && errors == 0 && unserved == 0 && worst <= (size == 16 ? 500 : 2000);
            if (!qualified)
            {
                owner._stop ??= recovery ? "recovery-quality" : "customer-quality";
            }

            var languages = rows.GroupBy(r => r.Language).Select(group => new { language = group.Key, customers = group.Count(), requests = group.Sum(r => r.LatenciesMs.Length), p99Ms = Percentile(group.SelectMany(r => r.LatenciesMs).Order().ToArray()), minRequests = group.Min(r => r.LatenciesMs.Length), maxRequests = group.Max(r => r.LatenciesMs.Length) }).ToArray();
            var summary = new
            {
                name,
                tenants = owner._tenants.Count,
                payloadChars = size,
                offeredSeconds = duration,
                elapsedSeconds = elapsed,
                completedDuration,
                requests = all.Length,
                errors,
                requestsPerSecond = rps,
                p99Ms = p99,
                worstTenantP99Ms = worst,
                unserved,
                minTenantRequests = rows.Min(r => r.LatenciesMs.Length),
                maxTenantRequests = rows.Max(r => r.LatenciesMs.Length),
                qualified,
                hostCpuSeconds,
                hostUserCpuSeconds,
                hostKernelCpuSeconds,
                languages,
                gcPauseMs = gcMs,
                allocationBytes,
                countersExcludeAnalysis = true
            };
            owner._summaries.Add(summary);
            await using (var file = File.Create(Path.Combine(owner.Output, name + ".json.gz")))
            {
                await using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
                {
                    await JsonSerializer.SerializeAsync(gzip, new
                    {
                        summary,
                        resources = _resources,
                        rows
                    });
                }
            }

            Console.WriteLine($"{name}: rps={rps:F1}, p99={p99:F2}ms, worst={worst:F2}ms, errors={errors}, stop={owner._stop ?? "none"}");

        }
        private static double Percentile(double[] data) => data.Length == 0 ? 0 : data[Math.Clamp((int)Math.Ceiling(data.Length * .99) - 1, 0, data.Length - 1)];
        private async Task SampleAsync()
        {
            try
            {
                while (!_observe.IsCancellationRequested)
                {
                    await SampleOnceAsync();
                    await Task.Delay(1000, _observe.Token);
                }
            }
            catch (OperationCanceledException) when (_observe.IsCancellationRequested) { }
            catch (Exception error) { Interlocked.CompareExchange(ref owner._stop, "resource-observer-" + error.GetType().Name, null); }
        }
        private async Task SampleOnceAsync()
        {
            var (rss, workerCpu) = ReadWorkerResources();
            using Process self = Process.GetCurrentProcess();
            double? free = await SystemFreeAsync();
            double? swap = await CapacityDiagnostics.SwapMiBAsync();
            long queued = Stopwatch.GetTimestamp();
            int threads = ThreadPool.ThreadCount;
            long pending = ThreadPool.PendingWorkItemCount;
            await Task.Run(static () => { }).WaitAsync(_observe.Token);
            double queueDelay = Stopwatch.GetElapsedTime(queued).TotalMilliseconds;
            _resources.Add(new Resource(Stopwatch.GetElapsedTime(_started).TotalSeconds, self.WorkingSet64, owner.Comparison is null ? rss : null, free, swap, self.TotalProcessorTime.TotalSeconds, owner.Comparison is null ? workerCpu : null, threads, pending, queueDelay));
            if (recovery)
            {
                return;
            }
            string? reason = ResourceStopReason(self.WorkingSet64, rss, free, swap);
            if (reason is not null)
            {
                Interlocked.CompareExchange(ref owner._stop, reason, null);
            }

        }
        private string? ResourceStopReason(long hostRss, long workerRss, double? freePercent, double? swapMiB)
        {
            const long bytesPerGiB = 1024L * 1024 * 1024;
            if (owner.Settings.MaximumHostRssGiB != 0 && hostRss > owner.Settings.MaximumHostRssGiB * bytesPerGiB)
            {
                return "host-rss-ceiling";
            }
            if (freePercent < 15)
            {
                return "system-memory-headroom";
            }
            if (swapMiB - owner.InitialSwapMiB > 1024)
            {
                return "system-swap-growth";
            }
            if (owner.Settings.MaximumSummedRssGiB != 0 && hostRss + workerRss > owner.Settings.MaximumSummedRssGiB * bytesPerGiB)
            {
                return "summed-rss-exploration-ceiling";
            }
            return null;
        }

        private (long Rss, double CpuSeconds) ReadWorkerResources()
        {
            long rss = 0;
            double workerCpu = 0;
            foreach (Tenant tenant in owner.Comparison is null ? owner._tenants : [])
            {
                int pid = int.Parse(tenant.Session.Instance[(tenant.Session.Instance.LastIndexOf("-p", StringComparison.Ordinal) + 2)..]);
                using Process worker = Process.GetProcessById(pid);
                rss += worker.WorkingSet64;
                workerCpu += worker.TotalProcessorTime.TotalSeconds;
            }
            return (rss, workerCpu);
        }
    }
}
