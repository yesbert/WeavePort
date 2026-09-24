using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class FunctionalScenarios
{
    internal static async Task RunAsync(PluginHost host, DemoCallbacks callbacks, Evidence evidence)
    {
        await using IPluginSession first = await BindAsync(host, callbacks, "A", "profile-1", "1");
        await using IPluginSession second = await BindAsync(host, callbacks, "A", "profile-2", "2");
        await using IPluginSession foreign = await BindAsync(host, callbacks, "B", "profile-1", "1");
        await evidence.CheckAsync("profile, version and tenant context", () => VerifyBindingIdentityAsync(first, second, foreign));
        await evidence.CheckAsync("deny forged tenant callback", async () => Assert((await CallAsync(first, "search", new { tenant = "B", query = "" })).Status == "denied", "foreign tenant accepted"));
        await evidence.CheckAsync("deny ungranted callback", async () => Assert((await CallAsync(first, "callback", new { operation = "admin.delete" })).Status == "denied", "ungranted operation accepted"));
        await evidence.CheckAsync("bound callback count", async () => Assert((await CallAsync(first, "callback-flood", new { })).Status == "protocol-error", "callback flood accepted"));
        await evidence.CheckAsync("reject oversized response", async () => Assert((await CallAsync(first, "oversize", new { })).Status == "protocol-error", "oversized frame accepted"));
        await evidence.CheckAsync("reject malformed response", async () => Assert((await CallAsync(first, "malformed", new { })).Status == "protocol-error", "malformed frame accepted"));
        await evidence.CheckAsync("caller cancellation", () => VerifyCancellationAsync(first));
        await evidence.CheckAsync("nested callback and same-session reentry", () => VerifyReentryAsync(first, second, callbacks));
        await evidence.CheckAsync("nested trace and shared callback budget", () => VerifyTraceAndBudgetAsync(first, second, callbacks));
        await evidence.CheckAsync("host-owned shared resource serialization", () => VerifySharedResourceAsync(first, second, callbacks));
        await evidence.CheckAsync("callback exception is contained", async () =>
            Assert((await CallAsync(first, "callback", new
            {
                operation = "throw"
            })).Status == "failed", "callback exception escaped"));
        await evidence.CheckAsync("uncertain external outcome, retry and compensation", () => VerifyCompensationAsync(first, callbacks));
        await evidence.CheckAsync("nested calls cannot cross tenants", () => VerifyCrossTenantDenialAsync(first, foreign, callbacks));
        await evidence.CheckAsync("reject oversized input without unbounded buffering", () => VerifyInputLimitAsync(first));
        await evidence.CheckAsync("isolated workspace and disable", () => VerifyDisableAsync(first, foreign));
    }

    private static Task<IPluginSession> BindAsync(PluginHost host, DemoCallbacks callbacks, string tenant, string profile, string version) =>
        host.BindAsync(new PluginContext(tenant, "demo", version, profile, JsonSerializer.SerializeToElement(new
        {
            secret = tenant + profile
        })),
            TestProfiles.Create("weaveport-poc-python:" + version, timeout: TimeSpan.FromSeconds(2)), callbacks, DemoCallbacks.Grants);
    private static Task<InvocationResult> CallAsync(IPluginSession session, string op, object payload) => session.InvokeAsync(op, JsonSerializer.SerializeToElement(payload));
    private static JsonElement Ok(InvocationResult result) => ContractChecks.Successful(result);
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
    private static async Task VerifyBindingIdentityAsync(IPluginSession first, IPluginSession second, IPluginSession foreign)
    {

        foreach ((IPluginSession session, string tenant, string profile, string version) in new[] { (first, "A", "profile-1", "1"), (second, "A", "profile-2", "2"), (foreign, "B", "profile-1", "1") })
        {
            JsonElement context = Ok(await CallAsync(session, "context", new
            {
            }));
            Assert(context.GetProperty("tenant").GetString() == tenant && context.GetProperty("profile").GetString() == profile && context.GetProperty("version").GetString() == version, "binding identity");
            Assert(context.GetProperty("configuration").GetProperty("secret").GetString() == tenant + profile, "profile secret");
        }

    }
    private static async Task VerifyCancellationAsync(IPluginSession first)
    {

        Ok(await CallAsync(first, "echo", new
        {
        }));
        using var cancellation = new CancellationTokenSource(100);
        Assert((await first.InvokeAsync("hang", JsonSerializer.SerializeToElement(new
        {
        }), cancellation.Token)).Status == "cancelled", "cancellation not observed");

    }
    private static async Task VerifyReentryAsync(IPluginSession first, IPluginSession second, DemoCallbacks callbacks)
    {

        callbacks.Nested = second;
        JsonElement value = Ok(await CallAsync(first, "callback", new
        {
            operation = "nested",
            args = new
            {
                amount = 5
            }
        }));
        Assert(value.GetProperty("Status").GetString() == "ok", "nested call failed");
        callbacks.Nested = first;
        value = Ok(await CallAsync(first, "callback", new
        {
            operation = "nested",
            args = new
            {
            }
        }));
        Assert(value.GetProperty("Status").GetString() == "busy", "same-session call deadlocked");
        callbacks.Nested = null;

    }
    private static async Task VerifyTraceAndBudgetAsync(IPluginSession first, IPluginSession second, DemoCallbacks callbacks)
    {

        callbacks.Nested = second;
        callbacks.NestedOperation = "trace";
        JsonElement trace = Ok(await CallAsync(first, "callback", new
        {
            operation = "nested",
            args = new
            {
            }
        }));
        Assert(trace.GetProperty("TraceId").GetString() == trace.GetProperty("Value").GetString(), "nested trace changed");
        Ok(await CallAsync(second, "callback-flood", new
        {
            count = 8
        }));
        callbacks.NestedOperation = "callback-flood";
        JsonElement budget = Ok(await CallAsync(first, "callback", new
        {
            operation = "nested",
            args = new
            {
                count = 8
            }
        }));
        Assert(budget.GetProperty("Status").GetString() == "protocol-error", "nested call reset callback budget");
        callbacks.Nested = null;
        callbacks.NestedOperation = "echo";

    }
    private static async Task VerifySharedResourceAsync(IPluginSession first, IPluginSession second, DemoCallbacks callbacks)
    {

        InvocationResult[] results = await Task.WhenAll(CallAsync(first, "callback", new
        {
            operation = "resource.use"
        }), CallAsync(second, "callback", new
        {
            operation = "resource.use"
        }));
        foreach (InvocationResult result in results)
        {
            Ok(result);
        }

        Assert(callbacks.MaximumResourceUsers == 1, "shared actuator used concurrently");

    }
    private static async Task VerifyCompensationAsync(IPluginSession first, DemoCallbacks callbacks)
    {

        InvocationResult uncertain = await CallAsync(first, "callback", new
        {
            operation = "external.perform",
            args = new
            {
                key = "action-1",
                loseResponse = true
            }
        });
        Assert(uncertain.Status == "timeout" && uncertain.MayHaveExecuted && callbacks.EffectCount == 1, "lost reply must retain external effect");
        JsonElement retried = Ok(await CallAsync(first, "callback", new
        {
            operation = "external.perform",
            args = new
            {
                key = "action-1"
            }
        }));
        Assert(retried.GetProperty("count").GetInt32() == 1 && callbacks.EffectCount == 1, "retry duplicated side effect");
        JsonElement failed = Ok(await CallAsync(first, "callback", new
        {
            operation = "external.compensate",
            args = new
            {
                key = "action-1",
                fail = true
            }
        }));
        Assert(failed.GetProperty("status").GetString() == "compensation-failed" && callbacks.EffectCount == 1, "compensation failure hidden");
        Ok(await CallAsync(first, "callback", new
        {
            operation = "external.compensate",
            args = new
            {
                key = "action-1"
            }
        }));
        Assert(callbacks.EffectCount == 0, "compensation not performed");

    }
    private static async Task VerifyCrossTenantDenialAsync(IPluginSession first, IPluginSession foreign, DemoCallbacks callbacks)
    {

        Assert((await CallAsync(foreign, "crash", new
        {
        })).Status == "failed", "foreign failure fixture did not execute");
        callbacks.Nested = foreign;
        JsonElement result = Ok(await CallAsync(first, "callback", new
        {
            operation = "nested",
            args = new
            {
            }
        }));
        Assert(result.GetProperty("Status").GetString() == "denied", "cross-tenant nested call accepted");
        Assert(!result.GetProperty("Value").EnumerateObject().Any(), "foreign diagnostic state leaked");
        callbacks.Nested = null;

    }
    private static async Task VerifyInputLimitAsync(IPluginSession first)
    {

        InvocationResult result = await CallAsync(first, "echo", new
        {
            text = new string('x', 1048577)
        });
        Assert(result.Status == "protocol-error", "oversized input accepted");

    }
    private static async Task VerifyDisableAsync(IPluginSession first, IPluginSession foreign)
    {

        Ok(await CallAsync(first, "workspace", new
        {
            text = "private"
        }));
        Assert(Ok(await CallAsync(foreign, "workspace", new
        {
        })).GetString() == "", "workspace leaked");
        await first.DisposeAsync();
        Assert((await CallAsync(first, "echo", new
        {
        })).Status == "disabled", "disabled session accepted work");
        Ok(await CallAsync(foreign, "echo", new
        {
        }));

    }

}
