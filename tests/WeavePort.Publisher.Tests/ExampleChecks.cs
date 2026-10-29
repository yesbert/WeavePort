using System.Security.Cryptography;
using WeavePort.Hosting;
using static PublisherFixture;

internal static class ExampleChecks
{
    internal static void Run(RSA key)
    {
        using var fixture = new PublisherFixture();
        fixture.Create();
        string destination = Path.Combine(fixture.Root, "installed");
        var installer = new StagedInstallation(destination, Trust(key));
        Refuse(() => installer.Install(fixture.Release(), "test", "1", "test/v1"), "publisher-signature-missing");
        Check(!Directory.Exists(destination), "Untrusted upload was promoted.");
        InstallationIdentity pin = ReleasePipeline.SealAndSign(fixture.Options(), key);
        Check(installer.Install(fixture.Release(), "test", "1", "test/v1").Identity == pin, "Trusted upload changed its pin.");
        Check(!Directory.Exists(fixture.Release()), "Promotion copied instead of moving exclusive staging.");
        var catalog = new InstalledPluginCatalog(destination, new Dictionary<string, string> { ["dotnet"] = Environment.ProcessPath! }, Trust(key));
        Check(catalog.Resolve("test", "1", "test/v1", pin).Identity == pin, "Promoted release could not resolve.");
    }
}
