using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class AdmissionScenarios
{
    internal static async Task RunAsync(DemoCallbacks callbacks, Evidence evidence)
    {
        await using var host = new PluginHost(1);
        await using IPluginSession a1 = await Bind(host, callbacks, "A", "1", "weaveport-poc-python:1");
        await using IPluginSession a2 = await Bind(host, callbacks, "A", "1", "weaveport-poc-python:1");
        await using IPluginSession b = await Bind(host, callbacks, "B", "1", "weaveport-poc-python:1");
        await evidence.Check("tenant admission spans multiple bindings", async () =>
        {
            ContractChecks.Successful(await Call(a1, "echo", new { }));
            Task<InvocationResult> occupied = Call(a1, "delay", new { ms = 300 });
            InvocationResult denied = await Call(a2, "echo", new { });
            if (denied.Status != "busy" || denied.MayHaveExecuted) throw new InvalidOperationException("Tenant admission not shared");
            ContractChecks.Successful(await Call(b, "echo", new { }));
            ContractChecks.Successful(await occupied);
        });
        await evidence.Check("uncooperative callback retains bounded admission", async () =>
        {
            using var deadline = new CancellationTokenSource(100);
            InvocationResult timed = await a1.InvokeAsync("callback", JsonSerializer.SerializeToElement(new { operation = "stubborn" }), deadline.Token);
            if (timed.Status != "cancelled") throw new InvalidOperationException("Callback wait did not cancel");
            InvocationResult saturated = await Call(a2, "search", new { query = "" });
            if (saturated.Status != "failed") throw new InvalidOperationException("Detached callback lost its admission lease");
            ContractChecks.Successful(await Call(b, "search", new { query = "" }));
        });
        await evidence.Check("dispose cancels active execution", async () =>
        {
            ContractChecks.Successful(await Call(a1, "echo", new { }));
            Task<InvocationResult> active = Call(a1, "hang", new { });
            await Task.Delay(50);
            await a1.DisposeAsync();
            if ((await active).Status != "disabled") throw new InvalidOperationException("Disposal did not cancel");
        });
        await evidence.Check("nested calls retain root callback grants", async () =>
        {
            await using var nestedHost = new PluginHost(4);
            await using IPluginSession narrow = await nestedHost.BindAsync(new PluginContext("rights", "demo", "1", "default", JsonSerializer.SerializeToElement(new { })),
                TestProfiles.Create("weaveport-poc-python:1"), callbacks, ["nested"]);
            await using IPluginSession broad = await Bind(nestedHost, callbacks, "rights", "1", "weaveport-poc-python:1");
            callbacks.Nested = broad;
            callbacks.NestedOperation = "search";
            InvocationResult result = await Call(narrow, "callback", new { operation = "nested", args = new { query = "" } });
            if (ContractChecks.Successful(result).GetProperty("Status").GetString() != "denied") throw new InvalidOperationException("Nested grants expanded authority");
            callbacks.Nested = null;
            callbacks.NestedOperation = "echo";
        });
        await evidence.Check("worker version handshake rejects mismatch", async () =>
        {
            await using IPluginSession wrong = await Bind(host, callbacks, "C", "2", "weaveport-poc-python:1");
            if ((await Call(wrong, "echo", new { })).Status != "protocol-error") throw new InvalidOperationException("Wrong worker version accepted");
        });
        await evidence.Check("resolved image survives mutable tag replacement", async () =>
        {
            string tag = "weaveport-poc-binding-test:" + Guid.NewGuid().ToString("N");
            try
            {
                await Docker(["tag", "weaveport-poc-python:1", tag]);
                await using IPluginSession old = await Bind(host, callbacks, "D", "1", tag);
                await Docker(["tag", "weaveport-poc-python:2", tag]);
                await using IPluginSession next = await Bind(host, callbacks, "E", "2", tag);
                ContractChecks.Successful(await Call(old, "echo", new { }));
                ContractChecks.Successful(await Call(next, "echo", new { }));
                await old.RestartAsync();
                ContractChecks.Successful(await Call(old, "echo", new { }));
            }
            finally { await Docker(["image", "rm", tag]); }
        });
    }

    private static Task<IPluginSession> Bind(PluginHost host, DemoCallbacks callbacks, string tenant, string version, string image) =>
        host.BindAsync(new PluginContext(tenant, "demo", version, "default", JsonSerializer.SerializeToElement(new { })), TestProfiles.Create(image), callbacks, DemoCallbacks.Grants);
    private static Task<InvocationResult> Call(IPluginSession session, string operation, object value) => session.InvokeAsync(operation, JsonSerializer.SerializeToElement(value));
    private static async Task Docker(string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using Process process = Process.Start(info)!;
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new IOException("Test image tag operation failed.");
    }
}
