using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal sealed partial class LoadExperiment(PluginHost host, TimeProvider clock, Telemetry telemetry, CancellationToken cancellation, Func<DockerProfile, DockerProfile>? configureProfile = null)
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
            if (telemetry.StopReason is not null)
            {
                return;
            }

            string language = choices[index % choices.Length];
            long started = clock.GetTimestamp();
            var profile = new DockerProfile("weaveport-poc-" + language + ":1", Timeout: TimeSpan.FromSeconds(10));
            if (configureProfile is not null)
            {
                profile = configureProfile(profile);
            }

            IPluginSession session = await host.BindAsync(new PluginContext("capacity-" + index, "demo", "1", "default", JsonSerializer.SerializeToElement(new
            {
            })),
                            profile, new Callbacks(), [], cancellationToken: token);
            lock (Tenants)
            {
                Tenants.Add(new Tenant(index, language, session, 0));
            }

            ContractChecks.Successful(await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
            {
                tenant = index
            }), token));
            lock (Tenants)
            {
                int position = Tenants.FindIndex(t => t.Index == index);
                Tenants[position] = new Tenant(index, language, session, clock.GetElapsedTime(started).TotalMilliseconds);
            }
        });
        lock (Tenants)
        {
            Tenants.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        if (Tenants.Count != target)
        {
            throw new IOException("Resource guard prevented full startup: " + telemetry.StopReason);
        }
    }

    internal string[] WorkerNames()
    {
        lock (Tenants)
        {
            return Tenants.Select(t => t.Session.Instance).Where(n => n.Length > 0).ToArray();
        }
    }

    internal async Task PrepareAsync()
    {
        await Parallel.ForEachAsync(Tenants, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellation }, async (tenant, token) =>
            ContractChecks.Successful(await tenant.Session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
            {
                tenant = tenant.Index
            }), token)));
    }

    internal Task<LoadResult> RunAsync(string workload, int durationSeconds, bool recovery = false) =>
        new Measurement(this, workload, durationSeconds, recovery).RunAsync();

    private TimeProvider Clock => clock;
    private Telemetry Telemetry => telemetry;
    private CancellationToken Cancellation => cancellation;

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
                InvocationResult reply = await tenant.Session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
                {
                    tenant = tenant.Index
                }), token);
                double end = (clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalMilliseconds;
                ContractChecks.Successful(reply);
                rows.Add(new
                {
                    tenant.Index,
                    hostStartUtcMs = start,
                    hostEndUtcMs = end,
                    timing = reply.Value.GetProperty("_diagnostic").Clone()
                });
            }
        });
        return rows.ToArray();
    }

    internal async Task ShrinkAsync(int keep)
    {
        Tenant[] removed = Tenants.Skip(keep).ToArray();
        await Parallel.ForEachAsync(removed, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (tenant, _) => await tenant.Session.DisposeAsync());
        lock (Tenants)
        {
            Tenants.RemoveRange(keep, Tenants.Count - keep);
        }
    }
}
