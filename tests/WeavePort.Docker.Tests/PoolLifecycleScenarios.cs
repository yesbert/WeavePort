using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class PoolLifecycleScenarios
{
    internal static async Task RunAsync(Evidence evidence)
    {
        foreach (string language in new[] { "csharp", "python", "typescript" })
        {
            await evidence.CheckAsync(language + ": shared pristine exclusive assignment and fresh customer state", () => VerifyPristineAssignmentAsync(language));
        }
        await evidence.CheckAsync("pool: memory budget rejects undispatched calls and releases on destruction", VerifyMemoryBudgetAsync);
        await evidence.CheckAsync("pool: idle release is opt-in and preserves immutable configuration", VerifyIdleRetentionAsync);
        await evidence.CheckAsync("pool: maintenance never evicts active invocation", VerifyActiveLeaseAsync);
        await evidence.CheckAsync("pool: expiry, target removal, version mismatch and registration churn", VerifyExpiryAndChurnAsync);
        await evidence.CheckAsync("pool: count and startup limits reject excess work", VerifyStartupLimitsAsync);
        await evidence.CheckAsync("pool: demand reclaims unrelated pristine capacity", VerifyPristineReclamationAsync);
        await evidence.CheckAsync("pool: automatic replenishment and idle release", VerifyAutomaticMaintenanceAsync);
        await evidence.CheckAsync("pool: per-tenant worker and memory quotas preserve other-tenant admission", VerifyTenantQuotasAsync);
        await LateCallbackAsync(evidence);
    }

    private static async Task LateCallbackAsync(Evidence evidence)
    {
        await evidence.CheckAsync("pool: detached callbacks retain admission and expired scopes cannot dispatch", VerifyLateCallbackAsync);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        long started = Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5))
            {
                throw new Exception("Maintenance deadline exceeded");
            }

            await Task.Delay(25);
        }
    }

    private static PluginHost Host(TimeProvider? clock = null) => new(options: new WorkerPoolOptions(MaximumWorkers: 8,
        MemoryBudgetMiB: 2048, MaximumPristineWorkers: 2, MaintenanceInterval: TimeSpan.FromHours(1)), timeProvider: clock);
    private static DockerProfile Profile(string language) => TestProfiles.Create("weaveport-poc-" + language + ":1", timeout: TimeSpan.FromSeconds(5));
    private static PluginContext Context(string tenant) => new(tenant, "demo", "1", "default", JsonSerializer.SerializeToElement(new { secret = tenant + "-secret" }));
    private static Task<IPluginSession> BindAsync(PluginHost host, string tenant, DockerProfile profile) => host.BindAsync(Context(tenant), profile,
        new Callback((call, _) => ValueTask.FromResult(call.Context.Configuration.GetProperty("secret"))), ["secret"]);
    private static Task<InvocationResult> CallAsync(IPluginSession session, string operation, object? value = null) => session.InvokeAsync(operation, JsonSerializer.SerializeToElement(value ?? new { }));
    private static JsonElement Ok(InvocationResult result) => ContractChecks.Successful(result);
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
    private static async Task<string[]> ContainersAsync()
    {
        using Process process = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "ps", "-aq", "--filter", "label=weaveport.poc=true" }, RedirectStandardOutput = true })!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new IOException("Container inventory failed");
        }

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
    private sealed class Callback(Func<HostCall, CancellationToken, ValueTask<JsonElement>> action) : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => action(call, cancellationToken);
    }
    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(GetTimestamp());
        internal void Advance(TimeSpan amount) => Interlocked.Add(ref _timestamp, (long)amount.TotalMilliseconds);
    }
    private static async Task VerifyPristineAssignmentAsync(string language)
    {

        await using var host = Host();
        DockerProfile profile = Profile(language);
        await host.PrewarmAsync(profile, "1", 2);
        Assert(host.Snapshot.Pristine == 2, "shared ready target");
        await using IPluginSession a = await BindAsync(host, "A", profile);
        await using IPluginSession b = await BindAsync(host, "B", profile);
        InvocationResult[] first = await Task.WhenAll(CallAsync(a, "counter"), CallAsync(b, "counter"));
        Assert(first.All(r => Ok(r).GetInt32() == 1) && a.Instance != b.Instance, "exclusive fresh counters");
        Assert(host.Snapshot.Pristine == 0 && host.Snapshot.Workers == 2, "no per-customer reserve");
        Ok(await CallAsync(a, "workspace", new
        {
            text = "A-private"
        }));
        Assert(Ok(await CallAsync(b, "workspace")).GetString() == "", "separate writable state");
        string old = a.Instance;
        await a.DisposeAsync();
        await host.PrewarmAsync(profile, "1", 1);
        await using IPluginSession c = await BindAsync(host, "C", profile);
        Assert(Ok(await CallAsync(c, "counter")).GetInt32() == 1 && c.Instance != old, "used instance never reassigned");
        Assert(Ok(await CallAsync(c, "workspace")).GetString() == "", "no prior files");
        Assert(Ok(await CallAsync(c, "context")).GetProperty("configuration").GetProperty("secret").GetString() == "C-secret", "bound secret");
        Assert(Ok(await CallAsync(c, "callback", new
        {
            operation = "secret",
            args = new
            {
            }
        })).GetString() == "C-secret", "callback authority");
        Assert((await CallAsync(a, "echo")).Status == "disabled", "disposed binding revoked");

    }
    private static async Task VerifyMemoryBudgetAsync()
    {

        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 4, MemoryBudgetMiB: 256,
            MaximumPristineWorkers: 1, MaintenanceInterval: TimeSpan.FromHours(1)));
        await host.PrewarmAsync(Profile("python"), "1", 1);
        await using IPluginSession a = await BindAsync(host, "A", Profile("python"));
        await using IPluginSession b = await BindAsync(host, "B", Profile("python"));
        Ok(await CallAsync(a, "counter"));
        InvocationResult rejected = await CallAsync(b, "counter");
        Assert(rejected.Status == "busy" && !rejected.MayHaveExecuted, "memory admission");
        await a.DisposeAsync();
        Assert(host.Snapshot.ReservedMemoryMiB == 0, "released reservation");
        Assert(Ok(await CallAsync(b, "counter")).GetInt32() == 1, "capacity reused with fresh environment");

    }
    private static async Task VerifyIdleRetentionAsync()
    {

        var clock = new ManualClock();
        await using var host = Host(clock);
        await using IPluginSession idle = await BindAsync(host, "idle", Profile("python") with
        {
            IdleTimeout = TimeSpan.FromSeconds(5)
        });
        await using IPluginSession stateful = await BindAsync(host, "stateful", Profile("python"));
        Ok(await CallAsync(idle, "counter"));
        Ok(await CallAsync(stateful, "counter"));
        string before = idle.Instance;
        clock.Advance(TimeSpan.FromSeconds(6));
        await host.MaintainAsync();
        Assert(host.Snapshot.Workers == 1, "only opted-in worker removed");
        Assert(Ok(await CallAsync(stateful, "counter")).GetInt32() == 2, "default state preserved");
        Assert(Ok(await CallAsync(idle, "counter")).GetInt32() == 1 && idle.Instance != before, "fresh restart after idle");
        Assert(Ok(await CallAsync(idle, "context")).GetProperty("configuration").GetProperty("secret").GetString() == "idle-secret", "original authority retained");

    }
    private static async Task VerifyActiveLeaseAsync()
    {

        var clock = new ManualClock();
        await using var host = Host(clock);
        await using IPluginSession a = await BindAsync(host, "A", Profile("python") with
        {
            IdleTimeout = TimeSpan.FromSeconds(1)
        });
        Ok(await CallAsync(a, "echo"));
        string before = a.Instance;
        Task<InvocationResult> active = CallAsync(a, "delay", new
        {
            ms = 300
        });
        clock.Advance(TimeSpan.FromSeconds(10));
        await host.MaintainAsync();
        Ok(await active);
        Assert(a.Instance == before && host.Snapshot.Workers == 1, "active lease protected");

    }
    private static async Task VerifyExpiryAndChurnAsync()
    {

        var clock = new ManualClock();
        await using var host = Host(clock);
        DockerProfile profile = Profile("python");
        await host.PrewarmAsync(profile, "1", 1);
        string[] before = await ContainersAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await host.MaintainAsync();
        string[] after = await ContainersAsync();
        Assert(before.Except(after).Count() == 1 && after.Except(before).Count() == 1, "expired pristine replaced");
        await host.PrewarmAsync(profile, "1", 0);
        Assert(host.Snapshot.Workers == 0, "target removed");
        try
        {
            await host.PrewarmAsync(profile, "wrong-version", 1);
            throw new Exception("version accepted");
        }
        catch (PluginVersionMismatchException error) { Assert(error.Mismatch.Expected == "wrong-version" && error.Mismatch.Advertised == "1" && host.Snapshot.Workers == 0, "failed startup released with diagnostic"); }
        await host.PrewarmAsync(profile, "wrong-version", 0);
        for (int i = 0; i < 100; i++)
        {
            await using IPluginSession session = await BindAsync(host, "churn-" + i, profile);
            if (i >= 10)
            {
                continue;
            }
            Ok(await CallAsync(session, "counter"));
        }
        Assert(host.Snapshot is { Workers: 0, Bindings: 0, Tenants: 0, ReservedMemoryMiB: 0 }, "no retained registrations or processes");

    }
    private static async Task VerifyStartupLimitsAsync()
    {

        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 4, MemoryBudgetMiB: 1024,
            MaximumPristineWorkers: 0, MaximumConcurrentStarts: 1, MaintenanceInterval: TimeSpan.FromHours(1)));
        IPluginSession[] bindings = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => BindAsync(host, "start-" + i, Profile("python"))));
        InvocationResult[] outcomes = await Task.WhenAll(bindings.Select(s => CallAsync(s, "counter")));
        Assert(outcomes.Count(r => r.Status == "ok") == 1 && outcomes.Count(r => r.Status == "busy" && !r.MayHaveExecuted) == 3, "bounded concurrent starts");

    }
    private static async Task VerifyPristineReclamationAsync()
    {

        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 1, MemoryBudgetMiB: 512,
            MaximumPristineWorkers: 1, MaintenanceInterval: TimeSpan.FromHours(1)));
        await host.PrewarmAsync(Profile("python"), "1", 1);
        await using IPluginSession a = await BindAsync(host, "A", Profile("csharp"));
        Ok(await CallAsync(a, "counter"));
        await using IPluginSession b = await BindAsync(host, "B", Profile("python"));
        Assert((await CallAsync(b, "counter")).Status == "busy" && host.Snapshot.Workers == 1, "count budget independent of memory ceiling");

    }
    private static async Task VerifyAutomaticMaintenanceAsync()
    {

        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 1,
            MaintenanceInterval: TimeSpan.FromMilliseconds(100)));
        await host.PrewarmAsync(Profile("python"), "1", 1);
        await using IPluginSession a = await BindAsync(host, "A", Profile("python"));
        Ok(await CallAsync(a, "counter"));
        await WaitAsync(() => host.Snapshot.Pristine == 1);
        await host.PrewarmAsync(Profile("python"), "1", 0);
        await a.DisposeAsync();
        await using IPluginSession idle = await BindAsync(host, "idle", Profile("python") with
        {
            IdleTimeout = TimeSpan.FromMilliseconds(100)
        });
        Ok(await CallAsync(idle, "counter"));
        await WaitAsync(() => host.Snapshot.Workers == 0);
        Assert(host.Snapshot.MaintenanceFailure is null, "background maintenance succeeded");

    }
    private static async Task VerifyTenantQuotasAsync()
    {

        foreach (bool memoryLimit in new[] { false, true })
        {
            await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumPristineWorkers: 0,
                MaximumWorkersPerTenant: memoryLimit ? 4 : 1, MemoryBudgetPerTenantMiB: memoryLimit ? 256 : 1024,
                MaintenanceInterval: TimeSpan.FromHours(1)));
            await using IPluginSession a = await BindAsync(host, "A", Profile("python"));
            await using IPluginSession excess = await BindAsync(host, "A", Profile("python"));
            await using IPluginSession b = await BindAsync(host, "B", Profile("python"));
            Ok(await CallAsync(a, "counter"));
            InvocationResult result = await CallAsync(excess, "counter");
            Assert(result.Status == "busy" && !result.MayHaveExecuted, "tenant quota rejected extra worker");
            Ok(await CallAsync(b, "counter"));
            await a.DisposeAsync();
            Ok(await CallAsync(excess, "counter"));
        }

    }
    private static async Task VerifyLateCallbackAsync()
    {

        await using var host = new PluginHost(1, new WorkerPoolOptions(MaintenanceInterval: TimeSpan.FromHours(1)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<InvocationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IPluginSession? replacement = null;
        var callback = new Callback(async (call, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            try
            {
                finished.TrySetResult(await CallAsync(replacement!, "counter"));
            }
            catch (Exception error) { finished.TrySetException(error); }
            return call.Context.Configuration;
        });
        IPluginSession a = await host.BindAsync(Context("A"), Profile("python") with
        {
            Timeout = TimeSpan.FromMilliseconds(500)
        }, callback, ["secret"]);
        try
        {
            Ok(await CallAsync(a, "echo"));
            Task<InvocationResult> pending = CallAsync(a, "callback", new
            {
                operation = "secret",
                args = new
                {
                }
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert((await pending).Status == "timeout", "origin timed out");
            await a.DisposeAsync();
            Assert(host.Snapshot.Tenants == 1 && host.Snapshot.Bindings == 0, "callback keeps admission alive");
            replacement = await BindAsync(host, "A", Profile("python"));
            Assert((await CallAsync(replacement, "callback", new
            {
                operation = "secret",
                args = new
                {
                }
            })).Status == "failed", "cannot bypass detached callback limit");
            release.TrySetResult();
            InvocationResult late = await finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert(late.Status == "denied" && !late.MayHaveExecuted, "expired nested scope denied");
        }
        finally
        {
            release.TrySetResult();
            await a.DisposeAsync();
            if (replacement is not null)
            {
                await replacement.DisposeAsync();
            }
        }

    }

}
