using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal sealed record Tenant(int Index, string Language, IPluginSession Session, double StartupMs);
internal sealed record TenantRequests(int Tenant, string Language, double[] LatenciesMs, Dictionary<string, int> StatusCounts, RequestObservation[]? Observations = null);
internal sealed record LoadResult(int Tenants, string Workload, double DurationSeconds, long Requests, long Errors,
    double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms, double MaxMs, int MinTenantRequests, int MaxTenantRequests, TenantRequests[] Raw, double OfferedSeconds, long WindowCompletions, double WindowRequestsPerSecond, double DrainSeconds, double GcPauseMs, long AllocatedBytes,
    double? WorstTenantP99Ms = null, int UnservedTenants = 0, bool GcCountersExcludeAnalysis = false)
{
    internal bool ExceedsQualityBoundary => Errors > Requests * 0.01 || UnservedTenants > 0 ||
        P99Ms > (Workload == "payload" ? 2000 : 500) || WorstTenantP99Ms > (Workload == "payload" ? 2000 : 500);
}

internal sealed class LoadExperiment(PluginHost host, TimeProvider clock, Telemetry telemetry, CancellationToken cancellation, Func<DockerProfile, DockerProfile>? configureProfile = null)
{
    internal List<Tenant> Tenants { get; } = [];
    private sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException("No callbacks in capacity fixtures");
    }

    internal async Task GrowAsync(int target, string languages)
    {
        int first = Tenants.Count;
        string[] choices = languages == "mixed" ? ["csharp", "python", "typescript"] : [languages];
        await Parallel.ForEachAsync(Enumerable.Range(first, target - first), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellation }, async (index, token) =>
        {
            if (telemetry.StopReason is not null) return;
            string language = choices[index % choices.Length];
            long started = clock.GetTimestamp();
            var profile = new DockerProfile("weaveport-poc-" + language + ":1", Timeout: TimeSpan.FromSeconds(10));
            if (configureProfile is not null) profile = configureProfile(profile);
            IPluginSession session = await host.BindAsync(new PluginContext("capacity-" + index, "demo", "1", "default", JsonSerializer.SerializeToElement(new { })),
                profile, new Callbacks(), [], token);
            lock (Tenants) Tenants.Add(new Tenant(index, language, session, 0));
            ContractChecks.Successful(await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { tenant = index }), token));
            lock (Tenants)
            {
                int position = Tenants.FindIndex(t => t.Index == index);
                Tenants[position] = new Tenant(index, language, session, clock.GetElapsedTime(started).TotalMilliseconds);
            }
        });
        lock (Tenants) Tenants.Sort((a, b) => a.Index.CompareTo(b.Index));
        if (Tenants.Count != target) throw new IOException("Resource guard prevented full startup: " + telemetry.StopReason);
    }

    internal string[] WorkerNames()
    {
        lock (Tenants) return Tenants.Select(t => t.Session.Instance).Where(n => n.Length > 0).ToArray();
    }

    internal async Task PrepareAsync()
    {
        await Parallel.ForEachAsync(Tenants, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellation }, async (tenant, token) =>
            ContractChecks.Successful(await tenant.Session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { tenant = tenant.Index }), token)));
    }

    internal async Task<LoadResult> RunAsync(string workload, int durationSeconds, bool recovery = false)
    {
        telemetry.Phase((recovery ? "recovery" : Tenants.Count.ToString()) + ":" + workload);
        long started = clock.GetTimestamp();
        TimeSpan gcPause = GC.GetTotalPauseDuration();
        long allocated = GC.GetTotalAllocatedBytes();
        long windowCompletions = 0;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TenantRequests>[] tasks = Tenants.Select(async tenant =>
        {
            JsonElement payload = workload == "delay" ? JsonSerializer.SerializeToElement(new { ms = 20 })
                : JsonSerializer.SerializeToElement(new { tenant = tenant.Index, text = new string('x', workload == "payload" ? RequestDiagnostics.PayloadChars : 16) });
            var latencies = new List<double>();
            List<RequestObservation>? observations = RequestDiagnostics.Enabled ? [] : null;
            var statuses = new Dictionary<string, int>();
            await ready.Task;
            while (clock.GetElapsedTime(started).TotalSeconds < durationSeconds && !cancellation.IsCancellationRequested && (recovery || telemetry.StopReason is null))
            {
                double startUtc = RequestDiagnostics.Enabled ? (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds : 0;
                double callStart = clock.GetElapsedTime(started).TotalMilliseconds;
                using RequestDiagnostics? diagnostic = RequestDiagnostics.Enabled ? new() : null;
                InvocationResult result = await tenant.Session.InvokeAsync(workload == "delay" ? "delay" : "echo", payload, cancellation);
                double callEnd = clock.GetElapsedTime(started).TotalMilliseconds;
                if (callEnd <= durationSeconds * 1000d) Interlocked.Increment(ref windowCompletions);
                string status = result.Status;
                if (status == "ok" && workload != "delay" && result.Value.GetProperty("tenant").GetInt32() != tenant.Index) status = "wrong-tenant";
                observations?.Add(diagnostic!.Finish(callStart, callEnd, status, startUtc, (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds, result.Value));
                latencies.Add(result.ElapsedMs);
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
            }
            return new TenantRequests(tenant.Index, tenant.Language, latencies.ToArray(), statuses, observations?.ToArray());
        }).ToArray();
        started = clock.GetTimestamp();
        ready.SetResult();
        TenantRequests[] rows = await Task.WhenAll(tasks);
        double elapsed = clock.GetElapsedTime(started).TotalSeconds;
        double loadGcPauseMs = (GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds;
        long loadAllocatedBytes = GC.GetTotalAllocatedBytes() - allocated;
        double[] times = rows.SelectMany(r => r.LatenciesMs).ToArray();
        if (times.Length == 0) throw new IOException("No requests completed: " + telemetry.StopReason);
        Array.Sort(times);
        double Percentile(double rank) => times[Math.Clamp((int)Math.Ceiling(rank / 100 * times.Length) - 1, 0, times.Length - 1)];
        long errors = rows.Sum(r => r.StatusCounts.Where(p => p.Key != "ok").Sum(p => p.Value));
        var result = new LoadResult(Tenants.Count, workload, elapsed, times.LongLength, errors, times.LongLength / elapsed,
            Percentile(50), Percentile(95), Percentile(99), times[^1],
            rows.Min(r => r.LatenciesMs.Length), rows.Max(r => r.LatenciesMs.Length), rows, durationSeconds, windowCompletions, windowCompletions / (double)durationSeconds, Math.Max(0, elapsed - durationSeconds),
            loadGcPauseMs, loadAllocatedBytes, WorstTenantP99(rows), rows.Count(r => r.LatenciesMs.Length == 0), true);
        Console.WriteLine($"{(recovery ? "recovery" : "load")} tenants={Tenants.Count} workload={workload} requests={result.Requests} errors={errors} rps={result.RequestsPerSecond:F1} p99={result.P99Ms:F2}ms");
        return result;
    }

    internal static double WorstTenantP99(TenantRequests[] rows) => rows.Where(r => r.LatenciesMs.Length != 0)
        .Max(r => ContractChecks.Percentile(r.LatenciesMs, 99));

    internal async Task<object[]> CalibrateAsync()
    {
        var rows = new System.Collections.Concurrent.ConcurrentBag<object>();
        await Parallel.ForEachAsync(Tenants, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellation }, async (tenant, token) =>
        {
            for (int n = 0; n < 5; n++)
            {
                double start = (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds;
                InvocationResult reply = await tenant.Session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { tenant = tenant.Index }), token);
                double end = (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds;
                ContractChecks.Successful(reply);
                rows.Add(new { tenant.Index, hostStartUtcMs = start, hostEndUtcMs = end, timing = reply.Value.GetProperty("_diagnostic").Clone() });
            }
        });
        return rows.ToArray();
    }

    internal async Task ShrinkAsync(int keep)
    {
        Tenant[] removed = Tenants.Skip(keep).ToArray();
        await Parallel.ForEachAsync(removed, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (tenant, _) => await tenant.Session.DisposeAsync());
        lock (Tenants) Tenants.RemoveRange(keep, Tenants.Count - keep);
    }
}
