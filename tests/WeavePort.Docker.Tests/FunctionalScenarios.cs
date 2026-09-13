using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class FunctionalScenarios
{
    internal static async Task RunAsync(PluginHost host, DemoCallbacks callbacks, Evidence evidence)
    {
        await using IPluginSession first = await Bind(host, callbacks, "A", "profile-1", "1");
        await using IPluginSession second = await Bind(host, callbacks, "A", "profile-2", "2");
        await using IPluginSession foreign = await Bind(host, callbacks, "B", "profile-1", "1");
        await evidence.Check("profile, version and tenant context", async () =>
        {
            foreach ((IPluginSession session, string tenant, string profile, string version) in new[] { (first, "A", "profile-1", "1"), (second, "A", "profile-2", "2"), (foreign, "B", "profile-1", "1") })
            {
                JsonElement context = Ok(await Call(session, "context", new { }));
                Assert(context.GetProperty("tenant").GetString() == tenant && context.GetProperty("profile").GetString() == profile && context.GetProperty("version").GetString() == version, "binding identity");
                Assert(context.GetProperty("configuration").GetProperty("secret").GetString() == tenant + profile, "profile secret");
            }
        });
        await evidence.Check("deny forged tenant callback", async () => Assert((await Call(first, "search", new { tenant = "B", query = "" })).Status == "denied", "foreign tenant accepted"));
        await evidence.Check("deny ungranted callback", async () => Assert((await Call(first, "callback", new { operation = "admin.delete" })).Status == "denied", "ungranted operation accepted"));
        await evidence.Check("bound callback count", async () => Assert((await Call(first, "callback-flood", new { })).Status == "protocol-error", "callback flood accepted"));
        await evidence.Check("reject oversized response", async () => Assert((await Call(first, "oversize", new { })).Status == "protocol-error", "oversized frame accepted"));
        await evidence.Check("reject malformed response", async () => Assert((await Call(first, "malformed", new { })).Status == "protocol-error", "malformed frame accepted"));
        await evidence.Check("caller cancellation", async () =>
        {
            Ok(await Call(first, "echo", new { }));
            using var cancellation = new CancellationTokenSource(100);
            Assert((await first.InvokeAsync("hang", JsonSerializer.SerializeToElement(new { }), cancellation.Token)).Status == "cancelled", "cancellation not observed");
        });
        await evidence.Check("nested callback and same-session reentry", async () =>
        {
            callbacks.Nested = second;
            JsonElement value = Ok(await Call(first, "callback", new { operation = "nested", args = new { amount = 5 } }));
            Assert(value.GetProperty("Status").GetString() == "ok", "nested call failed");
            callbacks.Nested = first;
            value = Ok(await Call(first, "callback", new { operation = "nested", args = new { } }));
            Assert(value.GetProperty("Status").GetString() == "busy", "same-session call deadlocked");
            callbacks.Nested = null;
        });
        await evidence.Check("nested trace and shared callback budget", async () =>
        {
            callbacks.Nested = second;
            callbacks.NestedOperation = "trace";
            JsonElement trace = Ok(await Call(first, "callback", new { operation = "nested", args = new { } }));
            Assert(trace.GetProperty("TraceId").GetString() == trace.GetProperty("Value").GetString(), "nested trace changed");
            Ok(await Call(second, "callback-flood", new { count = 8 }));
            callbacks.NestedOperation = "callback-flood";
            JsonElement budget = Ok(await Call(first, "callback", new { operation = "nested", args = new { count = 8 } }));
            Assert(budget.GetProperty("Status").GetString() == "protocol-error", "nested call reset callback budget");
            callbacks.Nested = null;
            callbacks.NestedOperation = "echo";
        });
        await evidence.Check("host-owned shared resource serialization", async () =>
        {
            InvocationResult[] results = await Task.WhenAll(Call(first, "callback", new { operation = "resource.use" }), Call(second, "callback", new { operation = "resource.use" }));
            foreach (InvocationResult result in results) Ok(result);
            Assert(callbacks.MaximumResourceUsers == 1, "shared actuator used concurrently");
        });
        await evidence.Check("callback exception is contained", async () =>
            Assert((await Call(first, "callback", new { operation = "throw" })).Status == "failed", "callback exception escaped"));
        await evidence.Check("uncertain external outcome, retry and compensation", async () =>
        {
            InvocationResult uncertain = await Call(first, "callback", new { operation = "external.perform", args = new { key = "action-1", loseResponse = true } });
            Assert(uncertain.Status == "timeout" && uncertain.MayHaveExecuted && callbacks.EffectCount == 1, "lost reply must retain external effect");
            JsonElement retried = Ok(await Call(first, "callback", new { operation = "external.perform", args = new { key = "action-1" } }));
            Assert(retried.GetProperty("count").GetInt32() == 1 && callbacks.EffectCount == 1, "retry duplicated side effect");
            JsonElement failed = Ok(await Call(first, "callback", new { operation = "external.compensate", args = new { key = "action-1", fail = true } }));
            Assert(failed.GetProperty("status").GetString() == "compensation-failed" && callbacks.EffectCount == 1, "compensation failure hidden");
            Ok(await Call(first, "callback", new { operation = "external.compensate", args = new { key = "action-1" } }));
            Assert(callbacks.EffectCount == 0, "compensation not performed");
        });
        await evidence.Check("nested calls cannot cross tenants", async () =>
        {
            Assert((await Call(foreign, "crash", new { })).Status == "failed", "foreign failure fixture did not execute");
            callbacks.Nested = foreign;
            JsonElement result = Ok(await Call(first, "callback", new { operation = "nested", args = new { } }));
            Assert(result.GetProperty("Status").GetString() == "denied", "cross-tenant nested call accepted");
            Assert(!result.GetProperty("Value").EnumerateObject().Any(), "foreign diagnostic state leaked");
            callbacks.Nested = null;
        });
        await evidence.Check("reject oversized input without unbounded buffering", async () =>
        {
            InvocationResult result = await Call(first, "echo", new { text = new string('x', 1048577) });
            Assert(result.Status == "protocol-error", "oversized input accepted");
        });
        await evidence.Check("isolated workspace and disable", async () =>
        {
            Ok(await Call(first, "workspace", new { text = "private" }));
            Assert(Ok(await Call(foreign, "workspace", new { })).GetString() == "", "workspace leaked");
            await first.DisposeAsync();
            Assert((await Call(first, "echo", new { })).Status == "disabled", "disabled session accepted work");
            Ok(await Call(foreign, "echo", new { }));
        });
    }

    private static Task<IPluginSession> Bind(PluginHost host, DemoCallbacks callbacks, string tenant, string profile, string version) =>
        host.BindAsync(new PluginContext(tenant, "demo", version, profile, JsonSerializer.SerializeToElement(new { secret = tenant + profile })),
            TestProfiles.Create("weaveport-poc-python:" + version, timeout: TimeSpan.FromSeconds(2)), callbacks, DemoCallbacks.Grants);
    private static Task<InvocationResult> Call(IPluginSession session, string op, object payload) => session.InvokeAsync(op, JsonSerializer.SerializeToElement(payload));
    private static JsonElement Ok(InvocationResult result) => ContractChecks.Successful(result);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
