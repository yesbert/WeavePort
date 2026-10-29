using WeavePort.Hosting;

internal static class SealingChecks
{
    internal static async Task RunAsync()
    {
        string root = Directory.CreateTempSubdirectory("wp-sealing-").FullName;
        try
        {
            await CheckDotnetAsync(root);
            CheckOffline(root, "python", "worker.py", "pyproject.toml", "[project]\nrequires-python = '>=3.11,<4'\n");
            CheckOffline(root, "node", "worker.mjs", "package.json", """{"engines":{"node":">=20 <23"}}""");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task CheckDotnetAsync(string root)
    {
        var options = Prepare(root, "dotnet", "worker.dll", "worker.runtimeconfig.json",
            """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        InstallationIdentity identity = CheckSeal(options);
        var catalog = new InstalledPluginCatalog(Path.Combine(root, "dotnet"),
            new Dictionary<string, string> { ["dotnet"] = Environment.ProcessPath! });
        if (catalog.Resolve("test", "1", "test/v1", identity).Identity != identity ||
            (await catalog.ResolveAsync("test", "1", "test/v1", identity)).Identity != identity)
        {
            throw new Exception("Sealed manifest did not resolve with its original pin.");
        }

        string manifest = Path.Combine(options.ReleaseDirectory, "installation.json");
        byte[] before = File.ReadAllBytes(manifest);
        File.WriteAllText(Path.Combine(options.ReleaseDirectory, "worker.runtimeconfig.json"), "{}");
        try
        {
            PluginInstallationSealer.Seal(options);
            throw new Exception("Invalid runtime declaration was accepted.");
        }
        catch (InvalidDataException error) when (error.Message.Contains("worker.runtimeconfig.json", StringComparison.Ordinal)) { }
        if (!before.SequenceEqual(File.ReadAllBytes(manifest)))
        {
            throw new Exception("Failed sealing replaced the previous manifest.");
        }
    }

    private static void CheckOffline(string root, string alias, string entry, string source, string content)
    {
        CheckSeal(Prepare(root, alias, entry, source, content));
    }

    private static PluginSealOptions Prepare(string root, string alias, string entry, string source, string content)
    {
        string release = Directory.CreateDirectory(Path.Combine(root, alias, "1")).FullName;
        File.WriteAllText(Path.Combine(release, entry), "fixture bytes; never execute");
        File.WriteAllText(Path.Combine(release, source), content);
        return new PluginSealOptions
        {
            ReleaseDirectory = release,
            Plugin = "test",
            Version = "1",
            Contract = "test/v1",
            EntryPoints = new Dictionary<string, string> { [alias] = entry },
            Launch = new() { Runtime = alias }
        };
    }

    private static InstallationIdentity CheckSeal(PluginSealOptions options)
    {
        InstallationIdentity identity = PluginInstallationSealer.Seal(options);
        byte[] before = File.ReadAllBytes(Path.Combine(options.ReleaseDirectory, "installation.json"));
        if (PluginInstallationSealer.Seal(options) != identity ||
            !before.SequenceEqual(File.ReadAllBytes(Path.Combine(options.ReleaseDirectory, "installation.json"))))
        {
            throw new Exception("Repeated sealing changed the manifest or identity.");
        }

        return identity;
    }
}
