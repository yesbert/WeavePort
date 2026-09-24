using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal sealed partial class LoadExperiment
{
    private sealed class Measurement(LoadExperiment owner, string workload, int durationSeconds, bool recovery)
    {
        private long _started;
        private long _windowCompletions;
        private TaskCompletionSource _ready = null!;
        internal async Task<LoadResult> RunAsync()
        {

            owner.Telemetry.Phase((recovery ? "recovery" : owner.Tenants.Count.ToString()) + ":" + workload);
            _started = owner.Clock.GetTimestamp();
            TimeSpan gcPause = GC.GetTotalPauseDuration();
            long allocated = GC.GetTotalAllocatedBytes();
            _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<TenantRequests>[] tasks = owner.Tenants.Select(RunTenantAsync).ToArray();

            _started = owner.Clock.GetTimestamp();
            _ready.SetResult();
            TenantRequests[] rows = await Task.WhenAll(tasks);
            double elapsed = owner.Clock.GetElapsedTime(_started).TotalSeconds;
            double loadGcPauseMs = (GC.GetTotalPauseDuration() - gcPause).TotalMilliseconds;
            long loadAllocatedBytes = GC.GetTotalAllocatedBytes() - allocated;
            return Analyze(rows, elapsed, loadGcPauseMs, loadAllocatedBytes);
        }
        private async Task<TenantRequests> RunTenantAsync(Tenant tenant)
        {
            JsonElement payload = workload == "delay" ? JsonSerializer.SerializeToElement(new
            {
                ms = 20
            })
                : JsonSerializer.SerializeToElement(new
                {
                    tenant = tenant.Index,
                    text = new string('x', workload == "payload" ? RequestDiagnostics.PayloadChars : 16)
                });
            var latencies = new List<double>();
            List<RequestObservation>? observations = RequestDiagnostics.Enabled ? [] : null;
            var statuses = new Dictionary<string, int>();
            await _ready.Task;
            while (owner.Clock.GetElapsedTime(_started).TotalSeconds < durationSeconds && !owner.Cancellation.IsCancellationRequested && (recovery || owner.Telemetry.StopReason is null))
            {
                await RecordRequestAsync(tenant, payload, latencies, statuses, observations);

            }
            return new TenantRequests(tenant.Index, tenant.Language, latencies.ToArray(), statuses, observations?.ToArray());
        }
        private async Task RecordRequestAsync(Tenant tenant, JsonElement payload, List<double> latencies,
            Dictionary<string, int> statuses, List<RequestObservation>? observations)
        {
            double startUtc = RequestDiagnostics.Enabled ? (owner.Clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds : 0;
            double callStart = owner.Clock.GetElapsedTime(_started).TotalMilliseconds;
            using RequestDiagnostics? diagnostic = RequestDiagnostics.Enabled ? new() : null;
            InvocationResult result = await tenant.Session.InvokeAsync(workload == "delay" ? "delay" : "echo", payload, owner.Cancellation);
            double callEnd = owner.Clock.GetElapsedTime(_started).TotalMilliseconds;
            if (callEnd <= durationSeconds * 1000d)
            {
                Interlocked.Increment(ref _windowCompletions);
            }

            string status = result.Status;
            if (status == "ok" && workload != "delay" && result.Value.GetProperty("tenant").GetInt32() != tenant.Index)
            {
                status = "wrong-tenant";
            }

            observations?.Add(diagnostic!.Finish(callStart, callEnd, status, startUtc, (owner.Clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds, result.Value));
            latencies.Add(result.ElapsedMs);
            statuses[status] = statuses.GetValueOrDefault(status) + 1;
        }
        private LoadResult Analyze(TenantRequests[] rows, double elapsed, double loadGcPauseMs, long loadAllocatedBytes)
        {
            double[] times = rows.SelectMany(r => r.LatenciesMs).ToArray();
            if (times.Length == 0)
            {
                throw new IOException("No requests completed: " + owner.Telemetry.StopReason);
            }

            Array.Sort(times);
            double Percentile(double rank) => times[Math.Clamp((int)Math.Ceiling(rank / 100 * times.Length) - 1, 0, times.Length - 1)];
            long errors = rows.Sum(r => r.StatusCounts.Where(p => p.Key != "ok").Sum(p => p.Value));
            var result = new LoadResult(owner.Tenants.Count, workload, elapsed, times.LongLength, errors, times.LongLength / elapsed,
                Percentile(50), Percentile(95), Percentile(99), times[^1],
                rows.Min(r => r.LatenciesMs.Length), rows.Max(r => r.LatenciesMs.Length), rows, durationSeconds, _windowCompletions, _windowCompletions / (double)durationSeconds, Math.Max(0, elapsed - durationSeconds),
                loadGcPauseMs, loadAllocatedBytes, WorstTenantP99(rows), rows.Count(r => r.LatenciesMs.Length == 0), true);
            Console.WriteLine($"{(recovery ? "recovery" : "load")} tenants={owner.Tenants.Count} workload={workload} requests={result.Requests} errors={errors} rps={result.RequestsPerSecond:F1} p99={result.P99Ms:F2}ms");
            return result;
        }
    }
}
