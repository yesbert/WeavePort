using System.Text.Json;
using WeavePort.Hosting;

internal static class ConsumerFindingChecks
{
    internal static async Task RunAsync()
    {
        foreach (var policy in Enum.GetValues<WorkerReusePolicy>())
        {
            var frame = JsonSerializer.SerializeToElement(new { type = "ready", protocol = policy == WorkerReusePolicy.Shared ? 2 : 1, pluginVersion = "1", concurrentCalls = 1, sessionCleanup = 1 });
            try { WorkerEnvelope.ValidateReady(frame, "1.0.0", policy); throw new Exception("Version accepted"); }
            catch (PluginVersionMismatchException error) when (error.Mismatch.Expected == "1.0.0" && error.Mismatch.Advertised == "1") { }
        }
        string root = Directory.CreateTempSubdirectory("wp-discovery-").FullName;
        try
        {
            string release = Path.Combine(root, "valid", "releases", "1");
            Directory.CreateDirectory(release);
            File.WriteAllText(Path.Combine(release, "worker.dll"), "fixture bytes");
            File.WriteAllText(Path.Combine(release, "worker.runtimeconfig.json"), """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
            PluginInstallationSealer.Seal(new() { ReleaseDirectory = release, Plugin = "valid", Version = "1", Contract = "test/v1", Launch = new() { Runtime = "dotnet" }, EntryPoints = new Dictionary<string, string> { ["dotnet"] = "worker.dll" } });
            File.WriteAllText(Path.Combine(root, "valid", "active.txt"), "1");
            Directory.CreateDirectory(Path.Combine(root, "unselected"));
            Directory.CreateDirectory(Path.Combine(root, "broken", "releases", "1"));
            File.WriteAllText(Path.Combine(root, "broken", "active.txt"), "1");
            File.WriteAllText(Path.Combine(root, "broken", "releases", "1", "installation.json"), "{");
            var catalog = new InstalledPluginCatalog(root, new Dictionary<string, string> { ["dotnet"] = Environment.ProcessPath! });
            foreach (var results in new[] { catalog.ListAll(), await catalog.ListAllAsync("test/v1") })
            {
                if (results.Count != 3 || results.Count(r => r.Installation is not null) != 1 || results.Count(r => r.Refusal is not null && r.Diagnostic is not null) != 2) throw new Exception("Incomplete discovery");
            }
            if (catalog.ListAll("other").Any(r => r.Installation is not null)) throw new Exception("Contract mismatch hidden");
            if (!OperatingSystem.IsWindows())
            {
                string frameworkRoot = Path.Combine(root, "framework-data");
                Directory.CreateDirectory(Path.Combine(frameworkRoot, "10.0.0"));
                File.WriteAllText(Path.Combine(frameworkRoot, "10.0.0", "Microsoft.NETCore.App.runtimeconfig.json"), "{}");
                string probe = Path.Combine(root, "probe");
                File.WriteAllText(probe, "#!/bin/sh\nprintf '%s\\n' 'Microsoft.NETCore.App 10.0.0 [" + frameworkRoot + "]'\n");
                File.SetUnixFileMode(probe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var malformedFramework = new InstalledPluginCatalog(root, new Dictionary<string, string> { ["dotnet"] = probe });
                foreach (var entries in new[] { malformedFramework.ListAll(), await malformedFramework.ListAllAsync() })
                    if (entries.Single(e => Path.GetFileName(e.Directory) == "valid").Refusal != "invalid-installation") throw new Exception("Malformed runtime interrupted discovery.");
            }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await catalog.ListAllAsync(cancellationToken: cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            try { new InstalledPluginCatalog(Path.Combine(root, "missing"), new Dictionary<string, string>()).ListAll(); throw new Exception("Root failure hidden"); }
            catch (DirectoryNotFoundException) { }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: structured version mismatch and complete diagnostic discovery");
    }
}
