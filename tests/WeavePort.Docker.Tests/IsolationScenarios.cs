using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Testing;

internal static class IsolationScenarios
{
    internal static async Task RunAsync(IPluginSession a, IPluginSession b, string language, Evidence evidence)
    {
        ContractChecks.Successful(await Call(b, "workspace", new { text = "B-persistent" }));
        int expectedCounter = 0;
        var baseline = new List<double>();
        for (int n = 0; n < 100; n++)
        {
            InvocationResult result = await Call(b, "counter", new { });
            RequireCounter(result, ++expectedCounter);
            baseline.Add(result.ElapsedMs);
        }
        double baselineP99 = ContractChecks.Percentile(baseline, 99);
        evidence.Measure(language + " B baseline", baseline.ToArray(), new { phase = "before-faults" });
        foreach (string fault in new[] { "exception", "crash", "hang", "memory", "slow-callback", "flood", "restart" })
        {
            await evidence.Check(language + ": isolate " + fault, async () =>
            {
                ContractChecks.Successful(await Call(a, "echo", new { }));
                string instance = b.Instance;
                var samples = new List<double>();
                Task attack = AttackAsync(a, fault, language, evidence);
                int n = 0;
                do
                {
                    InvocationResult result = await Call(b, "counter", new { });
                    RequireCounter(result, ++expectedCounter);
                    if (b.Instance != instance) throw new InvalidOperationException("B instance restarted");
                    samples.Add(result.ElapsedMs);
                    await Task.Delay(5);
                    n++;
                } while (!attack.IsCompleted || n < 30);
                await attack;
                string? workspace = ContractChecks.Successful(await Call(b, "workspace", new { })).GetString();
                if (workspace != "B-persistent") throw new InvalidOperationException("B lost workspace");
                evidence.Measure(language + " B during " + fault, samples.ToArray(), new { baselineP99, instanceUnchanged = true, statePreserved = true });
                if (ContractChecks.Percentile(samples, 99) > Math.Max(100, baselineP99 * 5))
                    throw new InvalidOperationException("B p99 interference threshold exceeded");
                ContractChecks.Successful(await Call(a, "echo", new { recovered = true }));
            });
        }
    }

    private static async Task AttackAsync(IPluginSession a, string fault, string language, Evidence evidence)
    {
        if (fault == "restart") { await a.RestartAsync(); return; }
        if (fault == "flood")
        {
            Task<InvocationResult> slow = Call(a, "delay", new { ms = 200 });
            InvocationResult[] excess = await Task.WhenAll(Enumerable.Range(0, 500).Select(_ => Call(a, "echo", new { })));
            if (excess.Any(r => r.Status != "busy")) throw new InvalidOperationException("Unbounded admission");
            ContractChecks.Successful(await slow);
            return;
        }
        InvocationResult result = fault == "slow-callback"
            ? await Call(a, "callback", new { operation = "slow" })
            : await Call(a, fault, new { });
        await File.WriteAllTextAsync(Path.Combine(evidence.DirectoryPath, language + "-" + fault + "-termination.json"), JsonSerializer.Serialize(result));
        string expected = fault is "hang" or "slow-callback" ? "timeout" : "failed";
        if (result.Status != expected) throw new InvalidOperationException($"Attack {fault}: expected {expected}, got {result.Status}");
    }

    private static void RequireCounter(InvocationResult result, int expected)
    {
        if (ContractChecks.Successful(result).GetInt32() != expected) throw new InvalidOperationException("B state changed");
    }
    private static Task<InvocationResult> Call(IPluginSession session, string op, object value) => session.InvokeAsync(op, JsonSerializer.SerializeToElement(value));
}
