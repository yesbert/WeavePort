using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class Pressure
{
    private sealed class Scenario(Evidence evidence, int repeat, PluginHost host,
        BoundaryCallbacks callbacks, IPluginSession victimSession, JsonElement victimInspect)
    {
        private readonly string _victimInstance = victimSession.Instance;
        internal async Task RunAsync()
        {
            await PhaseAsync("baseline", 20);
            foreach (string operation in new[] { "cpu", "memory", "pids", "threads", "tmpfs", "stdout", "stderr" })
            {
                await evidence.CheckAsync($"r{repeat}: bounded {operation} with victim continuity", () => VerifyPressureAsync(operation));
            }
            await evidence.CheckAsync($"r{repeat}: known victim path and stale invocation rejected", VerifyForeignAccessAsync);
            foreach (string operation in new[] { "callback-budget", "callback-duplicate" })
            {
                await evidence.CheckAsync($"r{repeat}: {operation} prevents excess dispatch", () => VerifyCallbackViolationAsync(operation));
            }
            await evidence.CheckAsync($"r{repeat}: revoked application permission and disposed binding", VerifyRevocationAsync);
            await evidence.CheckAsync($"r{repeat}: late callback loses invocation authority", VerifyLateCallbackAsync);
            await victimSession.DisposeAsync();
            await evidence.CheckAsync($"r{repeat}: all owned workers cleaned up", () => { Assert(host.Snapshot.Workers == 0, "Retained workers"); return Task.CompletedTask; });

        }
        private async Task VerifyPressureAsync(string operation)
        {
            await using IPluginSession attacker = await BindAsync("A");
            await ValueAsync(attacker, "echo", new
            {
            });
            Policy.Validate(await Policy.InspectAsync(attacker.Instance));
            int beforeSecurity = evidence.Failures;
            await SecurityObservations.RunAsync(evidence, $"r{repeat}-{operation}-attacker", attacker.Instance);
            Assert(evidence.Failures == beforeSecurity, "Attacker security preflight failed; no pressure dispatch");
            JsonElement pre = await Policy.CgroupAsync(attacker.Instance);
            Assert(pre.GetProperty("memory.max").GetString()!.Trim() == "268435456" && pre.GetProperty("memory.swap.max").GetString()!.Trim() == "0" &&
                pre.GetProperty("pids.max").GetString()!.Trim() == "64" && pre.GetProperty("cpu.max").GetString()!.Trim() == "50000 100000", "Effective cgroup limits differ");
            Task<InvocationResult> attack = attacker.InvokeAsync(operation, JsonSerializer.SerializeToElement(new
            {
            }));
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
            JsonElement? post = result.Status == "ok" ? await Policy.CgroupAsync(attacker.Instance) : null;
            await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, $"r{repeat}-{operation}.json"), JsonSerializer.Serialize(new
            {
                operation,
                result.Status,
                result.Value,
                pre,
                post,
                observations,
                hostRss
            }));
            evidence.Measure($"r{repeat}-{operation}-victim", observations.ToArray(), new
            {
                attackStatus = result.Status,
                sampledScope = "sequential victim calls while attack task outstanding, including failure cleanup"
            });
            VerifyPressureOutcome(operation, result, pre, post);
            await attacker.RestartAsync();
            Assert((await ValueAsync(attacker, "workspace", new
            {
            })).GetString() == "", "Restart retained writable state");
            await PhaseAsync(operation + "-recovery", 10);
        }
        private async Task VerifyForeignAccessAsync()
        {
            await using IPluginSession attacker = await BindAsync("A");
            JsonElement value = await ValueAsync(attacker, "foreign", new
            {
                victimPid = victimInspect.GetProperty("State").GetProperty("Pid").GetInt32()
            });
            Assert(value.EnumerateObject().All(p => p.Value.GetInt32() is 2 or 13), "Known foreign path opened");
            await ValueAsync(attacker, "remember", new
            {
            });
            Assert((await attacker.InvokeAsync("replay", JsonSerializer.SerializeToElement(new
            {
            }))).Status == "protocol-error", "Stale result accepted");
            await VictimAsync();
        }
        private async Task VerifyCallbackViolationAsync(string operation)
        {
            await using IPluginSession attacker = await BindAsync("A");
            int beforeCalls = callbacks.Calls;
            InvocationResult result = await attacker.InvokeAsync(operation, JsonSerializer.SerializeToElement(new
            {
            }));
            Assert(result.Status == "protocol-error", "Callback violation accepted");
            Assert(callbacks.Calls - beforeCalls == (operation == "callback-budget" ? 8 : 1), "Unexpected authorized callback dispatch count");
            await VictimAsync();
        }
        private async Task VerifyRevocationAsync()
        {
            await using IPluginSession attacker = await BindAsync("A");
            await ValueAsync(attacker, "callback", new
            {
                operation = "read",
                args = new
                {
                }
            });
            callbacks.Revoked = true;
            Assert((await attacker.InvokeAsync("callback", JsonSerializer.SerializeToElement(new
            {
                operation = "read"
            }))).Status == "denied", "Revocation ignored");
            await attacker.DisposeAsync();
            Assert((await attacker.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
            {
            }))).Status == "disabled", "Disposed binding dispatched");
            callbacks.Revoked = false;
            await VictimAsync();
        }
        private async Task VerifyLateCallbackAsync()
        {
            await using IPluginSession attacker = await BindAsync("A");
            callbacks.Target = attacker;
            using var cancel = new CancellationTokenSource();
            Task<InvocationResult> pending = attacker.InvokeAsync("callback", JsonSerializer.SerializeToElement(new
            {
                operation = "slow"
            }), cancel.Token);
            await callbacks.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancel.Cancel();
            try
            {
                Assert((await pending).Status == "cancelled", "Cancellation not surfaced");
            }
            finally { callbacks.Release.TrySetResult(); }
            Assert(await callbacks.Late.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "denied", "Late invocation scope retained authority");
            await VictimAsync();
        }
        private static void VerifyPressureOutcome(string operation, InvocationResult result, JsonElement pre, JsonElement? post)
        {
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
            if (operation == "cpu")
            {
                Assert(Counter(post!.Value, "cpu.stat", "nr_throttled") > Counter(pre, "cpu.stat", "nr_throttled"), "No observed CPU throttling");
            }

            if (operation is "pids" or "threads")
            {
                Assert(Counter(post!.Value, "pids.events", "max") > Counter(pre, "pids.events", "max"), "No observed PID-limit event");
            }

        }
        private Task<IPluginSession> BindAsync(string tenant) => Pressure.BindAsync(host, callbacks, tenant);
        private async Task<double> VictimAsync()
        {
            long start = Stopwatch.GetTimestamp();
            Assert((await ValueAsync(victimSession, "workspace", new
            {
            })).GetString() == "B-synthetic-private" && victimSession.Instance == _victimInstance, "Victim value/instance changed");
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Assert(elapsed < 2000, "Victim exceeded predeclared 2-second safety threshold");
            return elapsed;
        }
        private async Task PhaseAsync(string phase, int count)
        {
            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = await VictimAsync();
                await Task.Delay(20);
            }
            evidence.Measure($"r{repeat}-{phase}", values, new
            {
                safetyThresholdMs = 2000,
                scope = "correctness and coarse interference check, not capacity benchmark"
            });
        }
    }
}
