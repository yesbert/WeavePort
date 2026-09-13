using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class Pressure
{
    internal static async Task RunAsync(Evidence evidence, int repeat)
    {
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 2, MemoryBudgetMiB: 512, MaximumPristineWorkers: 0));
        var callbacks = new BoundaryCallbacks();
        await using IPluginSession b = await BindAsync("B");
        await ValueAsync(b, "workspace", new { text = "B-synthetic-private" });
        string victim = b.Instance;
        int priorFailures = evidence.Failures;
        await SecurityObservations.RunAsync(evidence, "extended-" + repeat, victim);
        Assert(evidence.Failures == priorFailures, "Effective security preflight failed; pressure tests stopped");
        JsonElement victimInspect = await Policy.InspectAsync(victim);
        Policy.Validate(victimInspect);
        await evidence.Check("r" + repeat + ": 16 unsafe policy negative controls", () =>
        {
            Assert(Policy.NegativeControls(victimInspect) == 16, "Negative controls missing");
            return Task.CompletedTask;
        });
        await PhaseAsync("baseline", 20);
        foreach (string operation in new[] { "cpu", "memory", "pids", "threads", "tmpfs", "stdout", "stderr" })
        {
            await evidence.Check($"r{repeat}: bounded {operation} with victim continuity", async () =>
            {
                await using IPluginSession a = await BindAsync("A");
                await ValueAsync(a, "echo", new { });
                Policy.Validate(await Policy.InspectAsync(a.Instance));
                int beforeSecurity = evidence.Failures;
                await SecurityObservations.RunAsync(evidence, $"r{repeat}-{operation}-attacker", a.Instance);
                Assert(evidence.Failures == beforeSecurity, "Attacker security preflight failed; no pressure dispatch");
                JsonElement pre = await Policy.CgroupAsync(a.Instance);
                Assert(pre.GetProperty("memory.max").GetString()!.Trim() == "268435456" && pre.GetProperty("memory.swap.max").GetString()!.Trim() == "0" &&
                    pre.GetProperty("pids.max").GetString()!.Trim() == "64" && pre.GetProperty("cpu.max").GetString()!.Trim() == "50000 100000", "Effective cgroup limits differ");
                Task<InvocationResult> attack = a.InvokeAsync(operation, JsonSerializer.SerializeToElement(new { }));
                var observations = new List<double>();
                var hostRss = new List<long>();
                do
                {
                    observations.Add(await VictimAsync());
                    using Process process = Process.GetCurrentProcess();
                    hostRss.Add(process.WorkingSet64);
                    Assert(hostRss[^1] < 512L * 1024 * 1024, "Runner crossed 512-MiB RSS safety bound");
                    await Task.Delay(20);
                } while (!attack.IsCompleted);
                InvocationResult result = await attack;
                JsonElement? post = result.Status == "ok" ? await Policy.CgroupAsync(a.Instance) : null;
                await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, $"r{repeat}-{operation}.json"), JsonSerializer.Serialize(new { operation, result.Status, result.Value, pre, post, observations, hostRss }));
                evidence.Measure($"r{repeat}-{operation}-victim", observations.ToArray(), new { attackStatus = result.Status, sampledScope = "sequential victim calls while attack task outstanding, including failure cleanup" });
                bool valid = operation switch
                {
                    "memory" => result.Status == "failed" && result.Value.TryGetProperty("oomKilled", out var oom) && oom.GetBoolean(),
                    "stdout" => result.Status == "protocol-error",
                    "pids" => result.Status == "ok" && result.Value.GetProperty("created").GetInt32() < 64 && result.Value.GetProperty("error").GetInt32() == 11,
                    "threads" => result.Status == "ok" && result.Value.GetProperty("created").GetInt32() < 64 && result.Value.GetProperty("denied").GetBoolean(),
                    "tmpfs" => result.Status == "ok" && result.Value.GetProperty("error").GetInt32() == 28 && result.Value.GetProperty("bytes").GetInt64() <= 16L * 1024 * 1024,
                    _ => result.Status == "ok"
                };
                Assert(valid, "Unexpected pressure outcome: " + operation + "=" + result.Status);
                if (operation == "cpu") Assert(Counter(post!.Value, "cpu.stat", "nr_throttled") > Counter(pre, "cpu.stat", "nr_throttled"), "No observed CPU throttling");
                if (operation is "pids" or "threads") Assert(Counter(post!.Value, "pids.events", "max") > Counter(pre, "pids.events", "max"), "No observed PID-limit event");
                await a.RestartAsync();
                Assert((await ValueAsync(a, "workspace", new { })).GetString() == "", "Restart retained writable state");
                await PhaseAsync(operation + "-recovery", 10);
            });
        }
        await evidence.Check($"r{repeat}: known victim path and stale invocation rejected", async () =>
        {
            await using IPluginSession a = await BindAsync("A");
            JsonElement value = await ValueAsync(a, "foreign", new { victimPid = victimInspect.GetProperty("State").GetProperty("Pid").GetInt32() });
            Assert(value.EnumerateObject().All(p => p.Value.GetInt32() is 2 or 13), "Known foreign path opened");
            await ValueAsync(a, "remember", new { });
            Assert((await a.InvokeAsync("replay", JsonSerializer.SerializeToElement(new { }))).Status == "protocol-error", "Stale result accepted");
            await VictimAsync();
        });
        foreach (string operation in new[] { "callback-budget", "callback-duplicate" })
        {
            await evidence.Check($"r{repeat}: {operation} prevents excess dispatch", async () =>
            {
                await using IPluginSession a = await BindAsync("A");
                int beforeCalls = callbacks.Calls;
                InvocationResult result = await a.InvokeAsync(operation, JsonSerializer.SerializeToElement(new { }));
                Assert(result.Status == "protocol-error", "Callback violation accepted");
                Assert(callbacks.Calls - beforeCalls == (operation == "callback-budget" ? 8 : 1), "Unexpected authorized callback dispatch count");
                await VictimAsync();
            });
        }
        await evidence.Check($"r{repeat}: revoked application permission and disposed binding", async () =>
        {
            await using IPluginSession a = await BindAsync("A");
            await ValueAsync(a, "callback", new { operation = "read", args = new { } });
            callbacks.Revoked = true;
            Assert((await a.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "read" }))).Status == "denied", "Revocation ignored");
            await a.DisposeAsync();
            Assert((await a.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { }))).Status == "disabled", "Disposed binding dispatched");
            callbacks.Revoked = false;
            await VictimAsync();
        });
        await evidence.Check($"r{repeat}: late callback loses invocation authority", async () =>
        {
            await using IPluginSession a = await BindAsync("A");
            callbacks.Target = a;
            using var cancel = new CancellationTokenSource();
            Task<InvocationResult> pending = a.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "slow" }), cancel.Token);
            await callbacks.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            try { Assert((await pending).Status == "cancelled", "Cancellation not surfaced"); }
            finally { callbacks.Release.TrySetResult(); }
            Assert(await callbacks.Late.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "denied", "Late invocation scope retained authority");
            await VictimAsync();
        });
        await b.DisposeAsync();
        await evidence.Check($"r{repeat}: all owned workers cleaned up", () => { Assert(host.Snapshot.Workers == 0, "Retained workers"); return Task.CompletedTask; });

        Task<IPluginSession> BindAsync(string tenant) => host.BindAsync(new PluginContext(tenant, "security", "1", "default", JsonSerializer.SerializeToElement(new { })),
            new DockerProfile("weaveport-poc-security-extended:1", Timeout: TimeSpan.FromSeconds(6)), callbacks, ["read", "slow"]);
        async Task<double> VictimAsync()
        {
            long start = Stopwatch.GetTimestamp();
            Assert((await ValueAsync(b, "workspace", new { })).GetString() == "B-synthetic-private" && b.Instance == victim, "Victim value/instance changed");
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert(elapsed < 2000, "Victim exceeded predeclared 2-second safety threshold");
            return elapsed;
        }
        async Task PhaseAsync(string phase, int count)
        {
            var values = new double[count];
            for (int i = 0; i < count; i++) { values[i] = await VictimAsync(); await Task.Delay(20); }
            evidence.Measure($"r{repeat}-{phase}", values, new { safetyThresholdMs = 2000, scope = "correctness and coarse interference check, not capacity benchmark" });
        }
    }

    private static long Counter(JsonElement value, string file, string name) => long.Parse(value.GetProperty(file).GetString()!.Split('\n').Single(l => l.StartsWith(name + " ", StringComparison.Ordinal)).Split(' ')[1]);
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task<JsonElement> ValueAsync(IPluginSession s, string op, object value)
    {
        InvocationResult result = await s.InvokeAsync(op, JsonSerializer.SerializeToElement(value));
        Assert(result.Status == "ok", "Unexpected status: " + result.Status);
        return result.Value;
    }

    private sealed class BoundaryCallbacks : IHostCallbacks
    {
        internal int Calls;
        internal bool Revoked;
        internal IPluginSession? Target;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<string> Late { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (Revoked) throw new UnauthorizedAccessException();
            if (call.Operation == "slow")
            {
                Started.TrySetResult();
                await Release.Task;
                Late.TrySetResult((await Target!.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { }))).Status);
            }
            return JsonSerializer.SerializeToElement(new { owner = call.Context.Tenant });
        }
    }
}
