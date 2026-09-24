using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Testing;

internal static class IsolationScenarios
{
    internal static Task RunAsync(IPluginSession a, IPluginSession b, string language, Evidence evidence) =>
        new Scenario(a, b, language, evidence).RunAsync();

    private sealed class Scenario(IPluginSession a, IPluginSession b, string language, Evidence evidence)
    {
        private int _expectedCounter;
        internal async Task RunAsync()
        {

            ContractChecks.Successful(await CallAsync(b, "workspace", new
            {
                text = "B-persistent"
            }));
            var baseline = new List<double>();
            for (int n = 0; n < 100; n++)
            {
                InvocationResult result = await CallAsync(b, "counter", new
                {
                });
                RequireCounter(result, ++_expectedCounter);
                baseline.Add(result.ElapsedMs);
            }
            double baselineP99 = ContractChecks.Percentile(baseline, 99);
            evidence.Measure(language + " B baseline", baseline.ToArray(), new
            {
                phase = "before-faults"
            });
            foreach (string fault in new[] { "exception", "crash", "hang", "memory", "slow-callback", "flood", "restart" })
            {
                await evidence.CheckAsync(language + ": isolate " + fault, async () =>
                {
                    await VerifyFaultAsync(fault, baselineP99);
                });
            }
        }
        private async Task VerifyFaultAsync(string fault, double baselineP99)
        {
            ContractChecks.Successful(await CallAsync(a, "echo", new
            {
            }));
            string instance = b.Instance;
            var samples = new List<double>();
            Task attack = AttackAsync(a, fault, language, evidence);
            int n = 0;
            do
            {
                InvocationResult result = await CallAsync(b, "counter", new
                {
                });
                RequireCounter(result, ++_expectedCounter);
                if (b.Instance != instance)
                {
                    throw new InvalidOperationException("B instance restarted");
                }

                samples.Add(result.ElapsedMs);
                await Task.Delay(5);
                n++;
            } while (!attack.IsCompleted || n < 30);
            await attack;
            string? workspace = ContractChecks.Successful(await CallAsync(b, "workspace", new
            {
            })).GetString();
            if (workspace != "B-persistent")
            {
                throw new InvalidOperationException("B lost workspace");
            }

            evidence.Measure(language + " B during " + fault, samples.ToArray(), new
            {
                baselineP99,
                instanceUnchanged = true,
                statePreserved = true
            });
            if (ContractChecks.Percentile(samples, 99) > Math.Max(100, baselineP99 * 5))
            {
                throw new InvalidOperationException("B p99 interference threshold exceeded");
            }

            ContractChecks.Successful(await CallAsync(a, "echo", new
            {
                recovered = true
            }));
        }
    }
    private static async Task AttackAsync(IPluginSession a, string fault, string language, Evidence evidence)
    {
        if (fault == "restart")
        {
            await a.RestartAsync();
            return;
        }
        if (fault == "flood")
        {
            await FloodAdmissionAsync(a);
            return;
        }
        InvocationResult result = fault == "slow-callback"
            ? await CallAsync(a, "callback", new
            {
                operation = "slow"
            })
            : await CallAsync(a, fault, new
            {
            });
        await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, language + "-" + fault + "-termination.json"), JsonSerializer.Serialize(result));
        string expected = fault is "hang" or "slow-callback" ? "timeout" : "failed";
        if (result.Status != expected)
        {
            throw new InvalidOperationException($"Attack {fault}: expected {expected}, got {result.Status}");
        }
    }

    private static async Task FloodAdmissionAsync(IPluginSession a)
    {
        Task<InvocationResult> slow = CallAsync(a, "delay", new
        {
            ms = 200
        });
        InvocationResult[] excess = await Task.WhenAll(Enumerable.Range(0, 500).Select(_ => CallAsync(a, "echo", new { })));
        if (excess.Any(r => r.Status != "busy"))
        {
            throw new InvalidOperationException("Unbounded admission");
        }

        ContractChecks.Successful(await slow);
    }
    private static void RequireCounter(InvocationResult result, int expected)
    {
        if (ContractChecks.Successful(result).GetInt32() != expected)
        {
            throw new InvalidOperationException("B state changed");
        }
    }
    private static Task<InvocationResult> CallAsync(IPluginSession session, string op, object value) => session.InvokeAsync(op, JsonSerializer.SerializeToElement(value));
}
