using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;
using WeavePort.Testing;

internal static partial class LocalVerification
{
    private sealed class Scenario(LocalConfiguration config, string output)
    {
        private readonly List<object> _checks = [];
        private int _failures;
        private readonly Callbacks _callbacks = new();
        private readonly PluginHost _host = new(options: new WorkerPoolOptions(MaintenanceInterval: TimeSpan.FromHours(1)));
        internal async Task<int> RunAsync()
        {
            await using var ownedHost = _host;
            string? oldCanary = Environment.GetEnvironmentVariable("WEAVEPORT_PARENT_CANARY");
            Environment.SetEnvironmentVariable("WEAVEPORT_PARENT_CANARY", "synthetic-parent-only");
            try
            {
                foreach (string language in new[] { "csharp", "python", "typescript" })
                {
                    await VerifyLanguageAsync(language);
                }
                await VerifyProtectionAsync();
                await VerifyLifecycleAsync();
            }
            finally { Environment.SetEnvironmentVariable("WEAVEPORT_PARENT_CANARY", oldCanary); }
            await _host.DisposeAsync();
            await CheckAsync("owned roots and reservations released", () =>
            {
                Assert(_host.Snapshot.Workers == 0 && (!Directory.Exists(config.WorkspaceRoot) || Directory.GetDirectories(config.WorkspaceRoot).Length == 0));
                return Task.CompletedTask;
            });
            await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
            {
                failures = _failures,
                checks = _checks,
                hostAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
                scope = "Trusted cooperative local processes, no OS sandbox or hard resource ceiling; no native memory-exhaustion attack."
            }, new JsonSerializerOptions { WriteIndented = true }));
            return _failures == 0 ? 0 : 1;

        }
        private async Task VerifyLanguageAsync(string language)
        {
            await using IPluginSession a = await BindAsync(language, "A");
            await using IPluginSession b = await BindAsync(language, "B");
            await CheckAsync(language + ": contract and callback", async () => Assert((await CallAsync(a, "search", new { query = "weave" }))[0].GetProperty("id").GetString() == "A-1"));
            await CheckAsync(language + ": environment not inherited", async () => Assert(!(await CallAsync(a, "environment", new { })).GetProperty("parentSecretPresent").GetBoolean()));
            await CheckAsync(language + ": independent workspace and context", async () =>
            {
                await CallAsync(a, "workspace", new
                {
                    text = "A-synthetic-private"
                });
                Assert((await CallAsync(b, "workspace", new
                {
                })).GetString() == "");
                Assert((await CallAsync(b, "context", new
                {
                })).GetProperty("configuration").GetProperty("marker").GetString() == "B-canary");
            });
            await CallAsync(b, "workspace", new
            {
                text = "B-private"
            });
            string victim = b.Instance;
            await VerifyFaultContinuityAsync(language, a, b, victim);
            await CheckAsync(language + ": restart and state replay", async () =>
                            {
                                await CallAsync(a, "workspace", new
                                {
                                    text = "old"
                                });
                                string old = a.Instance;
                                await a.RestartAsync();
                                Assert((await CallAsync(a, "workspace", new
                                {
                                })).GetString() == "" && a.Instance != old);
                                Assert((await CallAsync(a, "reduce", new
                                {
                                    state = 5,
                                    amount = 4,
                                    now = "2040-01-01"
                                })).GetProperty("state").GetInt32() == 9);
                            });
            await CheckAsync(language + ": forged callback denied", async () => Assert((await a.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "documents.read", args = new { tenant = "B" } }))).Status == "denied"));
            await CheckAsync(language + ": cancellation", async () =>
            {
                await CallAsync(a, "echo", new
                {
                });
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                Assert((await a.InvokeAsync("delay", JsonSerializer.SerializeToElement(new
                {
                    ms = 1000
                }), cancel.Token)).Status == "cancelled");
            });
        }
        private async Task VerifyFaultContinuityAsync(string language, IPluginSession a, IPluginSession b, string victim)
        {
            foreach ((string operation, string status) in new[] { ("crash", "failed"), ("exception", "failed"), ("malformed", "protocol-error"), ("hang", "timeout") })
            {
                await CheckAsync(language + ": " + operation + " and B continuity", async () =>
                                {
                                    InvocationResult result = await a.InvokeAsync(operation, JsonSerializer.SerializeToElement(new
                                    {
                                    }));
                                    Assert(result.Status == status);
                                    Assert((await CallAsync(b, "workspace", new
                                    {
                                    })).GetString() == "B-private" && b.Instance == victim);
                                });
            }


        }
        private async Task VerifyProtectionAsync()
        {
            await CheckAsync("untrusted profile rejected before launch", async () =>
            {
                try
                {
                    await _host.BindAsync(Context("X"), config.Profile("python") with
                    {
                        TrustedCode = false
                    }, _callbacks, []);
                }
                catch (NotSupportedException) { return; }
                throw new Exception("Untrusted execution accepted");
            });
            foreach (ExecutionProtections requirement in new[] { ExecutionProtections.RestrictedFileSystem, ExecutionProtections.DisabledNetwork, ExecutionProtections.HardResourceLimits })
            {
                await CheckAsync("unmet protection rejected for bind and prewarm: " + requirement, async () =>
                            {
                                int denied = 0;
                                try
                                {
                                    await _host.BindAsync(Context("X"), config.Profile("python"), _callbacks, [], requiredProtection: requirement);
                                }
                                catch (NotSupportedException) { denied++; }
                                try
                                {
                                    await _host.PrewarmAsync(config.Profile("python"), "1", 1, requiredProtection: requirement);
                                }
                                catch (NotSupportedException) { denied++; }
                                Assert(denied == 2);
                            });
            }

            await CheckAsync("missing executable rejected", async () =>
                        {
                            try
                            {
                                await _host.BindAsync(Context("X"), config.Profile("python") with
                                {
                                    Executable = Path.Combine(config.WorkspaceRoot, "missing")
                                }, _callbacks, []);
                            }
                            catch (FileNotFoundException) { return; }
                            throw new Exception("Missing prerequisite accepted");
                        });

        }
        private async Task VerifyLifecycleAsync()
        {
            await CheckAsync("equivalent profiles share pristine reserve without reuse", async () =>
            {
                await _host.PrewarmAsync(config.Profile("python"), "1", 1);
                Assert(_host.Snapshot.Pristine == 1);
                await using (IPluginSession a = await BindAsync("python", "pristine-A"))
                {
                    await CallAsync(a, "workspace", new
                    {
                        text = "pristine-A-private"
                    });
                    Assert(_host.Snapshot.Pristine == 0 && _host.Snapshot.Workers == 1);
                }
                await _host.PrewarmAsync(config.Profile("python"), "1", 0);
                await using IPluginSession b = await BindAsync("python", "pristine-B");
                Assert((await CallAsync(b, "workspace", new
                {
                })).GetString() == "");
            });
            if (config.UseUnixSocket)
            {
                await CheckAsync("incompatible socket worker times out without fallback", async () =>
                            {
                                var profile = new ProcessProfile(config.Python, ["-I", "-u", "-c", "import time; time.sleep(30)"], true, config.WorkspaceRoot, timeout: TimeSpan.FromMilliseconds(200)) { UseUnixSocket = true };
                                await using IPluginSession incompatible = await _host.BindAsync(Context("incompatible"), profile, _callbacks, []);
                                InvocationResult result = await incompatible.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
                                {
                                }));
                                Assert(result.Status == "timeout" && _host.Snapshot.Workers == 0);
                            });
            }

            await CheckAsync("idle policy destroys used process", async () =>
                        {
                            await using IPluginSession a = await _host.BindAsync(Context("idle"), config.Profile("python", idleTimeout: TimeSpan.FromMilliseconds(50)), _callbacks, []);
                            await CallAsync(a, "workspace", new
                            {
                                text = "old"
                            });
                            string old = a.Instance;
                            await Task.Delay(100);
                            await _host.MaintainAsync();
                            Assert((await CallAsync(a, "workspace", new
                            {
                            })).GetString() == "" && a.Instance != old);
                        });
        }
        private Task<IPluginSession> BindAsync(string language, string tenant) => _host.BindAsync(Context(tenant), config.Profile(language, TimeSpan.FromSeconds(2)), _callbacks, ["documents.read"]);
        private async Task CheckAsync(string name, Func<Task> action)
        {
            try
            {
                await action();
                _checks.Add(new
                {
                    name,
                    passed = true
                });
                Console.WriteLine("PASS " + name);
            }
            catch (Exception error) { _failures++; _checks.Add(new { name, passed = false, error = error.GetType().Name }); Console.WriteLine("FAIL " + name + ": " + error.GetType().Name); }
        }
    }
}
