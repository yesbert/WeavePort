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
        await using IPluginSession a1 = await BindAsync(host, callbacks, "A", "1", "weaveport-poc-python:1");
        await using IPluginSession a2 = await BindAsync(host, callbacks, "A", "1", "weaveport-poc-python:1");
        await using IPluginSession b = await BindAsync(host, callbacks, "B", "1", "weaveport-poc-python:1");
        await evidence.CheckAsync("tenant admission spans multiple bindings", () => VerifySharedAdmissionAsync(a1, a2, b));
        await evidence.CheckAsync("uncooperative callback retains bounded admission", () => VerifyDetachedCallbackAsync(a1, a2, b));
        await evidence.CheckAsync("dispose cancels active execution", () => VerifyActiveDisposalAsync(a1));
        await evidence.CheckAsync("nested calls retain root callback grants", () => VerifyNestedGrantsAsync(callbacks));
        await evidence.CheckAsync("worker version handshake rejects mismatch", () => VerifyVersionMismatchAsync(host, callbacks));
        await evidence.CheckAsync("resolved image survives mutable tag replacement", () => VerifyImagePinAsync(host, callbacks));
    }

    private static Task<IPluginSession> BindAsync(PluginHost host, DemoCallbacks callbacks, string tenant, string version, string image) =>
        host.BindAsync(new PluginContext(tenant, "demo", version, "default", JsonSerializer.SerializeToElement(new
        {
        })), TestProfiles.Create(image), callbacks, DemoCallbacks.Grants);
    private static Task<InvocationResult> CallAsync(IPluginSession session, string operation, object value) => session.InvokeAsync(operation, JsonSerializer.SerializeToElement(value));
    private static async Task DockerAsync(string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new IOException("Test image tag operation failed.");
        }
    }
    private static async Task VerifySharedAdmissionAsync(IPluginSession a1, IPluginSession a2, IPluginSession b)
    {

        ContractChecks.Successful(await CallAsync(a1, "echo", new
        {
        }));
        Task<InvocationResult> occupied = CallAsync(a1, "delay", new
        {
            ms = 300
        });
        InvocationResult denied = await CallAsync(a2, "echo", new
        {
        });
        if (denied.Status != "busy" || denied.MayHaveExecuted)
        {
            throw new InvalidOperationException("Tenant admission not shared");
        }

        ContractChecks.Successful(await CallAsync(b, "echo", new
        {
        }));
        ContractChecks.Successful(await occupied);

    }
    private static async Task VerifyDetachedCallbackAsync(IPluginSession a1, IPluginSession a2, IPluginSession b)
    {

        using var deadline = new CancellationTokenSource(100);
        InvocationResult timed = await a1.InvokeAsync("callback", JsonSerializer.SerializeToElement(new
        {
            operation = "stubborn"
        }), deadline.Token);
        if (timed.Status != "cancelled")
        {
            throw new InvalidOperationException("Callback wait did not cancel");
        }

        InvocationResult saturated = await CallAsync(a2, "search", new
        {
            query = ""
        });
        if (saturated.Status != "failed")
        {
            throw new InvalidOperationException("Detached callback lost its admission lease");
        }

        ContractChecks.Successful(await CallAsync(b, "search", new
        {
            query = ""
        }));

    }
    private static async Task VerifyActiveDisposalAsync(IPluginSession a1)
    {

        ContractChecks.Successful(await CallAsync(a1, "echo", new
        {
        }));
        Task<InvocationResult> active = CallAsync(a1, "hang", new
        {
        });
        await Task.Delay(50);
        await a1.DisposeAsync();
        if ((await active).Status != "disabled")
        {
            throw new InvalidOperationException("Disposal did not cancel");
        }

    }
    private static async Task VerifyNestedGrantsAsync(DemoCallbacks callbacks)
    {

        await using var nestedHost = new PluginHost(4);
        await using IPluginSession narrow = await nestedHost.BindAsync(new PluginContext("rights", "demo", "1", "default", JsonSerializer.SerializeToElement(new
        {
        })),
            TestProfiles.Create("weaveport-poc-python:1"), callbacks, ["nested"]);
        await using IPluginSession broad = await BindAsync(nestedHost, callbacks, "rights", "1", "weaveport-poc-python:1");
        callbacks.Nested = broad;
        callbacks.NestedOperation = "search";
        InvocationResult result = await CallAsync(narrow, "callback", new
        {
            operation = "nested",
            args = new
            {
                query = ""
            }
        });
        if (ContractChecks.Successful(result).GetProperty("Status").GetString() != "denied")
        {
            throw new InvalidOperationException("Nested grants expanded authority");
        }

        callbacks.Nested = null;
        callbacks.NestedOperation = "echo";

    }
    private static async Task VerifyVersionMismatchAsync(PluginHost host, DemoCallbacks callbacks)
    {

        await using IPluginSession wrong = await BindAsync(host, callbacks, "C", "2", "weaveport-poc-python:1");
        if ((await CallAsync(wrong, "echo", new
        {
        })).Status != "version-mismatch")
        {
            throw new InvalidOperationException("Wrong worker version accepted");
        }

    }
    private static async Task VerifyImagePinAsync(PluginHost host, DemoCallbacks callbacks)
    {

        string tag = "weaveport-poc-binding-test:" + Guid.NewGuid().ToString("N");
        try
        {
            await DockerAsync(["tag", "weaveport-poc-python:1", tag]);
            await using IPluginSession old = await BindAsync(host, callbacks, "D", "1", tag);
            await DockerAsync(["tag", "weaveport-poc-python:2", tag]);
            await using IPluginSession next = await BindAsync(host, callbacks, "E", "2", tag);
            ContractChecks.Successful(await CallAsync(old, "echo", new
            {
            }));
            ContractChecks.Successful(await CallAsync(next, "echo", new
            {
            }));
            await old.RestartAsync();
            ContractChecks.Successful(await CallAsync(old, "echo", new
            {
            }));
        }
        finally { await DockerAsync(["image", "rm", tag]); }

    }

}
