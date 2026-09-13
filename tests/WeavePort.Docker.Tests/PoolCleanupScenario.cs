using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class PoolCleanupScenario
{
    internal static Task RunAsync(Evidence evidence) => evidence.Check("pool: uncertain removal quarantines capacity until confirmed cleanup", async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "weaveport-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string flag = Path.Combine(directory, "blocked-instance");
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        using Process which = Process.Start(new ProcessStartInfo("which") { ArgumentList = { "docker" }, RedirectStandardOutput = true })!;
        string docker = (await which.StandardOutput.ReadToEndAsync()).Trim(); await which.WaitForExitAsync();
        if (which.ExitCode != 0) throw new IOException("Docker not found");
        string wrapper = Path.Combine(directory, "docker");
        await File.WriteAllTextAsync(wrapper, "#!/bin/sh\nif [ -f " + Quote(flag) + " ]; then\n IFS= read -r target < " + Quote(flag) +
            "\n case \"$*\" in *\"$target\"*) case \"$1\" in rm|ps) echo simulated-cleanup-failure >&2; exit 73;; esac;; esac\nfi\nexec " + Quote(docker) + " \"$@\"\n");
        using Process chmod = Process.Start(new ProcessStartInfo("chmod") { ArgumentList = { "+x", wrapper } })!; await chmod.WaitForExitAsync();
        await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 1, MemoryBudgetMiB: 256,
            MaximumPristineWorkers: 0, MaintenanceInterval: TimeSpan.FromHours(1)));
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + originalPath);
            IPluginSession a = await Bind("A");
            ContractChecks.Successful(await a.InvokeAsync("counter", JsonSerializer.SerializeToElement(new { })));
            await File.WriteAllTextAsync(flag, a.Instance + "\n");
            try { await a.DisposeAsync(); throw new Exception("Removal failure was hidden"); }
            catch (IOException) { }
            if (host.Snapshot is not { Workers: 1, Quarantined: 1, ReservedMemoryMiB: 256, Bindings: 0 })
                throw new Exception("Unconfirmed removal released its reservation");
            string? endpoint = TestProfiles.SocketTransport is { } transport ? Path.Combine(transport.LocalDirectory, a.Instance) : null;
            if (endpoint is not null && !Directory.Exists(endpoint)) throw new Exception("Unconfirmed removal deleted the endpoint");
            await using IPluginSession b = await Bind("B");
            InvocationResult denied = await b.InvokeAsync("counter", JsonSerializer.SerializeToElement(new { }));
            if (denied.Status != "busy" || denied.MayHaveExecuted) throw new Exception("Quarantine admitted extra work");
            File.Delete(flag);
            await host.MaintainAsync();
            if (endpoint is not null && Directory.Exists(endpoint)) throw new Exception("Confirmed removal retained the endpoint");
            if (host.Snapshot.Workers != 0) throw new Exception("Confirmed cleanup did not release capacity");
            ContractChecks.Successful(await b.InvokeAsync("counter", JsonSerializer.SerializeToElement(new { })));
        }
        finally
        {
            File.Delete(flag);
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(directory, true);
        }
        Task<IPluginSession> Bind(string tenant) => host.BindAsync(new PluginContext(tenant, "demo", "1", "default", JsonSerializer.SerializeToElement(new { })),
            TestProfiles.Create("weaveport-poc-python:1"), new NoCallbacks(), []);
    });
    private static string Quote(string text) => "'" + text.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
