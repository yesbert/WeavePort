using System.Security.Cryptography;
using WeavePort.Hosting;

internal sealed class PublisherFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("wp-publisher-").FullName;
    internal string Release(string plugin = "test") => Path.Combine(Root, plugin, "releases", "1");
    internal string Manifest(string plugin = "test") => Path.Combine(Release(plugin), "installation.json");
    internal string Signature(string plugin = "test") => Path.Combine(Release(plugin), "installation.signature.json");
    internal string Selector(string plugin = "test") => Path.Combine(Root, plugin, "active.txt");
    internal static PluginPublisherPolicy Trust(RSA key, string publisher = "publisher") =>
        PluginPublisherPolicy.RequireTrustedPublisher([new(publisher, key.ExportSubjectPublicKeyInfo())]);

    internal PluginSealOptions Options(string plugin = "test") => new()
    {
        ReleaseDirectory = Release(plugin),
        Plugin = plugin,
        Version = "1",
        Contract = "test/v1",
        EntryPoints = new Dictionary<string, string> { ["dotnet"] = "worker.dll" },
        Launch = new()
        {
            Runtime = "dotnet"
        }
    };

    internal InstallationIdentity Create(string plugin = "test")
    {
        Directory.CreateDirectory(Release(plugin));
        File.WriteAllText(Path.Combine(Release(plugin), "worker.dll"), "fixture bytes, never execute");
        File.WriteAllText(Path.Combine(Release(plugin), "worker.runtimeconfig.json"),
            """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}""");
        InstallationIdentity identity = PluginInstallationSealer.Seal(Options(plugin));
        File.WriteAllText(Selector(plugin), "1");
        return identity;
    }

    internal InstallationIdentity Sign(RSA key, string plugin = "test") =>
        PluginInstallationSigner.Sign(Release(plugin), plugin, "1", "test/v1", key);
    internal PluginInstallationVerification Verify(PluginPublisherPolicy policy, string plugin = "test", InstallationIdentity? pin = null) =>
        PluginInstallationVerifier.Verify(Release(plugin), plugin, "1", "test/v1", policy, pin);
    internal InstalledPluginCatalog Catalog(PluginPublisherPolicy policy, string? runtime = null) =>
        new(Root, new Dictionary<string, string> { ["dotnet"] = runtime ?? Environment.ProcessPath! }, policy);

    internal static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    internal static void Refuse(Action action, string code)
    {
        try
        {
            action();
        }
        catch (PluginSignatureException error) when (error.ErrorCode == code) { return; }
        throw new Exception("Expected publisher refusal: " + code);
    }

    internal static void Invalid(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected invalid installation refusal.");
    }

    public void Dispose() => Directory.Delete(Root, true);
}
