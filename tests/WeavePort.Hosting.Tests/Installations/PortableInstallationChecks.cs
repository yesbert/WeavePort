using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeavePort.Hosting;

internal static class PortableInstallationChecks
{
    internal static async Task RunAsync()
    {
        RuntimeRequirementChecks.Run();
        string root = Directory.CreateTempSubdirectory("wp-portable-").FullName;
        try
        {
            await CheckAsync(root);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS portable sealing, resolution, pins, strict runtime and probe boundaries");
    }

    private static async Task CheckAsync(string root)
    {
        string release = Path.Combine(root, "1");
        Directory.CreateDirectory(release);
        File.WriteAllText(Path.Combine(release, "worker.dll"), "independent bundle bytes");
        File.WriteAllText(Path.Combine(release, "worker.runtimeconfig.json"), """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        var options = new PluginSealOptions { ReleaseDirectory = release, Plugin = "test", Version = "1", Contract = "test/v1", EntryPoints = new Dictionary<string, string> { ["dotnet"] = "worker.dll" }, Launch = new() { Runtime = "dotnet" } };
        InstallationIdentity identity = PluginInstallationSealer.Seal(options);
        string path = Path.Combine(release, "installation.json");
        byte[] bytes = File.ReadAllBytes(path);
        if (identity.Digest != Convert.ToHexString(SHA256.HashData(bytes)))
        {
            throw new Exception("Identity isn't manifest hash");
        }

        if (PluginInstallationSealer.Seal(options) != identity)
        {
            throw new Exception("Non-deterministic manifest");
        }

        CheckPathIndependence(root, release, options, identity);
        var runtime = new Dictionary<string, string> { ["dotnet"] = Environment.ProcessPath! };
        var catalog = new InstalledPluginCatalog(root, runtime);
        if (catalog.Resolve("test", "1", "test/v1", identity).Identity != identity || (await catalog.ResolveAsync("test", "1", "test/v1", identity)).Identity != identity)
        {
            throw new Exception("Portable resolve");
        }

        await RejectAsync(() => new InstalledPluginCatalog(root, new Dictionary<string, string>()).ResolveAsync("test", "1", "test/v1"));
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                await catalog.ResolveAsync("test", "1", "test/v1", cancellationToken: cancelled.Token);
                throw new Exception("Cancellation ignored");
            }
            catch (OperationCanceledException) { }
        }
        await CheckRuntimeAndArtifactMutationAsync(options, catalog, runtime, release, path, bytes);
        await CheckIndependentPinAsync(root, runtime, path, bytes);
        string selector = Path.Combine(root, "active.txt");
        File.WriteAllText(selector, "previous");
        File.WriteAllText(Path.Combine(release, "worker.dll"), "mutated");
        await RejectAsync(() => catalog.ActivateAsync(selector, "test", "1", "test/v1"));
        if (File.ReadAllText(selector) != "previous")
        {
            throw new Exception("Refused activation changed selector");
        }

        await CheckDeclarationsAsync(root);
        if (!OperatingSystem.IsWindows())
        {
            await CheckProbeAsync(root);
        }
    }

    private static async Task CheckRuntimeAndArtifactMutationAsync(PluginSealOptions options, InstalledPluginCatalog catalog,
        Dictionary<string, string> runtime, string release, string path, byte[] bytes)
    {
        PluginInstallationSealer.Seal(options with
        {
            StrictRuntimeFiles = runtime
        });
        var strict = JsonNode.Parse(File.ReadAllText(path))!;
        strict["Runtimes"]!["dotnet"]!["Sha256"] = new string('0', 64);
        File.WriteAllText(path, strict.ToJsonString());
        await RejectAsync(() => catalog.ResolveAsync("test", "1", "test/v1"));
        File.WriteAllBytes(path, bytes);
        File.WriteAllText(Path.Combine(release, "worker.dll"), "changed");
        await RejectAsync(() => catalog.ResolveAsync("test", "1", "test/v1"));
        File.WriteAllText(Path.Combine(release, "worker.dll"), "independent bundle bytes");
        var changed = JsonNode.Parse(File.ReadAllText(path))!;
        changed["Runtimes"]!["dotnet"]!["Requirement"] = "[]";
        File.WriteAllText(path, changed.ToJsonString());
        await RejectAsync(() => catalog.ResolveAsync("test", "1", "test/v1"));
        File.WriteAllBytes(path, bytes);
    }

    private static async Task CheckIndependentPinAsync(string root, Dictionary<string, string> runtime, string path, byte[] bytes)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "portable-manifest.json");
        string external = Path.Combine(root, "sdk-code");
        File.WriteAllText(external, "external sdk bytes");
        File.Copy(fixture, path, true);
        var independent = new InstalledPluginCatalog(root, new Dictionary<string, string>(runtime) { ["sdk/code"] = external });
        var expectedPin = new InstallationIdentity("test", "1", "test/v1", File.ReadAllText(fixture + ".sha256").Trim());
        if ((await independent.ResolveAsync("test", "1", "test/v1", expectedPin)).Identity != expectedPin)
        {
            throw new Exception("Independent fixture pin changed");
        }

        File.WriteAllText(external, "swapped sdk");
        await RejectAsync(() => independent.ResolveAsync("test", "1", "test/v1"));
        File.WriteAllBytes(path, bytes);
    }

    private static async Task CheckDeclarationsAsync(string root)
    {
        string release = Path.Combine(root, "other", "1");
        Directory.CreateDirectory(release);
        File.WriteAllText(Path.Combine(release, "entry.py"), "raise Exception('must not run')");
        File.WriteAllText(Path.Combine(release, "pyproject.toml"), "[project]\nrequires-python = '>=3.11,<4'\n");
        var options = new PluginSealOptions { ReleaseDirectory = release, Plugin = "test", Version = "1", Contract = "test/v1", EntryPoints = new Dictionary<string, string> { ["python"] = "entry.py" }, Launch = new() { Runtime = "python" } };
        PluginInstallationSealer.Seal(options);
        byte[] before = File.ReadAllBytes(Path.Combine(release, "installation.json"));
        File.WriteAllText(Path.Combine(release, "pyproject.toml"), "[project]\nrequires-python = 'invalid'\n");
        try
        {
            PluginInstallationSealer.Seal(options);
            throw new Exception("Invalid declaration accepted");
        }
        catch (InvalidDataException) { }
        if (!before.SequenceEqual(File.ReadAllBytes(Path.Combine(release, "installation.json"))))
        {
            throw new Exception("Failed seal mutated manifest");
        }

        foreach (string declaration in new[] { "[project]\n", "[project]\ndynamic = ['requires-python']\n", "[project]\nrequires-python = '>=3.11'\ndynamic = ['requires-python']\n", "[project\n" })
        {
            File.WriteAllText(Path.Combine(release, "pyproject.toml"), declaration);
            try
            {
                PluginInstallationSealer.Seal(options);
                throw new Exception("Invalid Python metadata accepted");
            }
            catch (InvalidDataException) { }
        }
        File.WriteAllText(Path.Combine(release, "entry.mjs"), "throw Error('must not run')");
        foreach (string range in new[] { ">=20 <23", "^20.1 || ~22.0", "20.x", "20 - 22", ">=22.0.0-rc.1" })
        {
            File.WriteAllText(Path.Combine(release, "package.json"), JsonSerializer.Serialize(new
            {
                engines = new
                {
                    node = range
                }
            }));
            PluginInstallationSealer.Seal(options with
            {
                EntryPoints = new Dictionary<string, string> { ["node"] = "entry.mjs" },
                Launch = new()
                {
                    Runtime = "node"
                }
            });
        }
        await Task.CompletedTask;
    }

    private static async Task CheckProbeAsync(string root)
    {
        string script = Path.Combine(root, "probe");
        void Script(string body)
        {
            File.WriteAllText(script, "#!/bin/sh\n" + body + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        Script("printf 'v22.1.0\\n'");
        if (RuntimeProbe.Run(script, "node") != "v22.1.0" || await RuntimeProbe.RunAsync(script, "node", default) != "v22.1.0")
        {
            throw new Exception("Probe result");
        }

        File.WriteAllText(Path.Combine(root, "cwd-marker"), "approved runtime directory");
        Script("[ -f cwd-marker ] || exit 7; printf 'v22.1.0\\n'");
        if (RuntimeProbe.Run(script, "node") != "v22.1.0" || await RuntimeProbe.RunAsync(script, "node", default) != "v22.1.0")
        {
            throw new Exception("Probe did not use the approved executable directory");
        }

        Script("exit 3");
        await RejectAsync(() => RuntimeProbe.RunAsync(script, "node", default));
        Script("/usr/bin/yes abcdefghijklmnopqrstuvwxyz");
        await RejectAsync(() => RuntimeProbe.RunAsync(script, "node", default));
        Script("/bin/sleep 30");
        var clock = Stopwatch.StartNew();
        await RejectAsync(() => RuntimeProbe.RunAsync(script, "node", default));
        if (clock.Elapsed > TimeSpan.FromSeconds(10))
        {
            throw new Exception("Probe bound");
        }
    }

    private static async Task RejectAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected installation refusal");
    }
    private static void CheckPathIndependence(string root, string release, PluginSealOptions options, InstallationIdentity identity)
    {
        string moved = Path.Combine(root, "moved", "1");
        Directory.CreateDirectory(moved);
        foreach (string file in Directory.GetFiles(release))
        {
            File.Copy(file, Path.Combine(moved, Path.GetFileName(file)));
        }

        if (PluginInstallationSealer.Seal(options with
        {
            ReleaseDirectory = moved
        }) != identity)
        {
            throw new Exception("Path bound manifest");
        }

    }

}
