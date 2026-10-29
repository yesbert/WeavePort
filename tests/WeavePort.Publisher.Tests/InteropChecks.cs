using WeavePort.Hosting;
using static PublisherFixture;

internal static class InteropChecks
{
    internal static void Run()
    {
        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var publisher = new TrustedPluginPublisher("independent-openssl", File.ReadAllBytes(Path.Combine(fixtures, "publisher.der")));
        var result = PluginInstallationVerifier.Verify(Path.Combine(fixtures, "release"), "interop", "1", "test/v1",
            PluginPublisherPolicy.RequireTrustedPublisher([publisher]));
        Check(result.Publisher?.Publisher == "independent-openssl", "Independent signature fixture failed.");
        Check(result.Identity.Digest == File.ReadAllText(Path.Combine(fixtures, "manifest.sha256")).Trim(), "Independent manifest identity changed.");
    }
}
