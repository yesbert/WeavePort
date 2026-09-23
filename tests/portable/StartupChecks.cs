using System.Text.Json;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class StartupChecks
{
    internal static async Task RunAsync(string work, string node)
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var ownership in Enum.GetValues<WorkerReusePolicy>())
        {
            string root = Path.Combine(work, "node-" + ownership);
            string release = Path.Combine(root, "1");
            File.WriteAllText(Path.Combine(release, "package.json"), "{\"type\":\"module\",\"engines\":{\"node\":\">=20 <30\"}}");
            string entry = Path.Combine(release, "entry.mjs");
            string worker = File.ReadAllText(entry).Replace("await app.run();", "app.function('crash', async () => { process.exit(1); });\nawait app.run();");
            File.WriteAllText(entry, worker);
            PluginInstallationSealer.Seal(new()
            {
                ReleaseDirectory = release, Plugin = "test", Version = "1", Contract = "test", EntryPoints = new Dictionary<string, string> { ["node"] = "entry.mjs" },
                Launch = new() { Runtime = "node", Ownership = [ownership] }
            });
            string state = Path.Combine(root, "runtime-version");
            string marker = Path.Combine(root, "starts");
            string wrapper = Path.Combine(root, "node-wrapper");
            File.WriteAllText(wrapper, "#!/bin/sh\nif [ \"$1\" = --version ]; then /bin/cat " + Quote(state) + "; exit 0; fi\nprintf 'start\\n' >> " + Quote(marker) + "\nexec " + Quote(node) + " \"$@\"\n");
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(state, "v24.0.0\n");
            var catalog = new InstalledPluginCatalog(root, new Dictionary<string, string> { ["node"] = wrapper });
            var installation = await catalog.ResolveAsync("test", "1", "test");
            File.WriteAllText(state, "v99.0.0\n");
            await AttemptAsync(installation, ownership, marker, false, state);
            if (File.Exists(marker)) throw new Exception("Incompatible initial runtime executed plugin");
            File.WriteAllText(state, "v24.0.0\n");
            await AttemptAsync(installation, ownership, marker, true, state);
            if (File.ReadAllLines(marker).Length != 1) throw new Exception("Incompatible replacement executed plugin");
            Console.WriteLine($"PASS {ownership}: version change after resolution refused before initial and replacement execution");
        }
    }

    private static async Task AttemptAsync(InstalledPlugin installation, WorkerReusePolicy ownership, string marker, bool replace, string state)
    {
        await using var host = new PluginHost();
        var approval = new PluginApproval { TrustedCode = true, Ownership = ownership, MaximumRestarts = 1, InvocationTimeout = TimeSpan.FromSeconds(3) };
        ISharedPlugin? shared = null;
        IBoundPluginClient? client = null;
        try
        {
            if (ownership == WorkerReusePolicy.Shared)
            {
                shared = await host.ShareAsync(installation, approval, JsonSerializer.SerializeToElement(new { }), new NoCallbacks(), []);
                client = shared.For("tenant");
            }
            else client = await host.BindAsync(installation, approval, new("tenant", JsonSerializer.SerializeToElement(new { })), new NoCallbacks(), []);
            if (replace)
            {
                await client.CallAsync("echo", JsonSerializer.SerializeToElement(1));
                if (!File.Exists(marker)) throw new Exception("Initial process did not start");
                File.WriteAllText(state, "v99.0.0\n");
                try { await client.CallAsync("crash", JsonSerializer.SerializeToElement(1)); } catch (PluginCallException) { }
            }
            try { await client.CallAsync("echo", JsonSerializer.SerializeToElement(1)); throw new Exception("Expected runtime refusal"); }
            catch (PluginCallException) { }
        }
        catch (InvalidDataException) when (!replace && ownership == WorkerReusePolicy.Shared) { }
        finally
        {
            if (client is not null) await client.DisposeAsync();
            if (shared is not null) await shared.DisposeAsync();
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
}
