using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

internal static class SocketScenarios
{
    internal static async Task RunAsync(Evidence evidence)
    {
        if (TestProfiles.SocketTransport is not { } transport) return;
        foreach (string language in new[] { "csharp", "python", "typescript" })
        {
            await evidence.Check(language + ": private read-only socket mount and endpoint cleanup", async () =>
            {
                await using var host = new PluginHost();
                IPluginSession a = await BindAsync(host, "socket-A", TestProfiles.Create("weaveport-poc-" + language + ":1"));
                await using IPluginSession b = await BindAsync(host, "socket-B", TestProfiles.Create("weaveport-poc-" + language + ":1"));
                await InvokeAsync(a); await InvokeAsync(b);
                using JsonDocument mounts = JsonDocument.Parse(await ResourceChecks.DockerAsync(["inspect", "--format", "{{json .Mounts}}", a.Instance]));
                JsonElement[] entries = mounts.RootElement.EnumerateArray().ToArray();
                if (entries.Length != 1 || entries[0].GetProperty("Destination").GetString() != "/run/weaveport" || entries[0].GetProperty("RW").GetBoolean() ||
                    entries[0].GetProperty("Source").GetString() != Path.Combine(transport.DockerDirectory, a.Instance)) throw new Exception("Unexpected endpoint exposure");
                // .NET unlinks the listening socket after its single accept; the connected stream remains live.
                string check = "test -d /run/weaveport && test ! -e /var/run/docker.sock && test ! -e \"$1\" && ! touch /run/weaveport/forbidden 2>/dev/null";
                await ResourceChecks.DockerAsync(["exec", "--user", "65532:65532", a.Instance, "/bin/sh", "-c", check, "check", Path.Combine(transport.DockerDirectory, b.Instance, "p.sock")]);
                string old = a.Instance;
                await a.DisposeAsync();
                if (Directory.Exists(Path.Combine(transport.LocalDirectory, old))) throw new Exception("Endpoint not removed");
                await InvokeAsync(b);
            });
        }
        await evidence.Check("socket: transport selection separates pristine workers", async () =>
        {
            await using var host = new PluginHost(options: new WorkerPoolOptions(MaintenanceInterval: TimeSpan.FromHours(1)));
            DockerProfile socket = TestProfiles.Create("weaveport-poc-python:1");
            DockerProfile cli = socket with { SocketTransport = null };
            await host.PrewarmAsync(cli, "1", 1);
            await host.PrewarmAsync(socket, "1", 1);
            await using var selected = await BindAsync(host, "selected", socket);
            await InvokeAsync(selected);
            using JsonDocument mounts = JsonDocument.Parse(await ResourceChecks.DockerAsync(["inspect", "--format", "{{json .Mounts}}", selected.Instance]));
            if (mounts.RootElement.GetArrayLength() != 1 || host.Snapshot.Pristine != 1) throw new Exception("Transport pool keys mixed");
        });
        await evidence.Check("socket: invalid mount fails without endpoint leak", async () =>
        {
            string[] before = Directory.GetDirectories(transport.LocalDirectory).Order().ToArray();
            await using var host = new PluginHost();
            DockerProfile invalid = TestProfiles.Create("weaveport-poc-python:1", timeout: TimeSpan.FromSeconds(2)) with
            { SocketTransport = transport with { DockerDirectory = "/weaveport-deliberately-missing" } };
            await using var session = await BindAsync(host, "invalid", invalid);
            InvocationResult result = await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { }));
            if (result.Status is not ("failed" or "timeout") || host.Snapshot.Workers != 0 || !before.SequenceEqual(Directory.GetDirectories(transport.LocalDirectory).Order()))
                throw new Exception("Startup failure leaked or fell back");
        });
    }

    private static Task<IPluginSession> BindAsync(PluginHost host, string tenant, DockerProfile profile) => host.BindAsync(
        new PluginContext(tenant, "demo", "1", "default", JsonSerializer.SerializeToElement(new { })), profile, new NoCallbacks(), []);
    private static async Task InvokeAsync(IPluginSession session) => ContractChecks.Successful(await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { text = "owned" })));
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
