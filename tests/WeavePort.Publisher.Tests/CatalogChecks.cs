using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WeavePort.Hosting;
using static PublisherFixture;

internal static class CatalogChecks
{
    internal static async Task RunAsync(RSA first, RSA second, RSA unknown)
    {
        using var fixture = new PublisherFixture();
        InstallationIdentity pin = fixture.Create();
        var trust = Trust(first);
        var catalog = fixture.Catalog(trust);
        Refuse(() => fixture.Catalog(trust, "/nonexistent-runtime").Resolve("test", "1", "test/v1"), "publisher-signature-missing");
        fixture.Sign(first);
        Check(catalog.Resolve("test", "1", "test/v1", pin).Publisher?.Publisher == "publisher", "Sync resolution lost publisher.");
        Check((await catalog.ResolveAsync("test", "1", "test/v1", pin)).Identity == pin, "Async pinned resolve failed.");
        Check(catalog.List("test/v1").Count == 1 && (await catalog.ListAsync("test/v1")).Count == 1, "Verified list failed.");
        string selector = Path.Combine(fixture.Root, "selection.txt");
        catalog.Activate(selector, "test", "1", "test/v1");
        await catalog.ActivateAsync(selector, "test", "1", "test/v1");
        var withdrawn = fixture.Catalog(Trust(second));
        Refuse(() => withdrawn.Resolve("test", "1", "test/v1", pin), "publisher-key-untrusted");
        Refuse(() => withdrawn.Activate(selector, "test", "1", "test/v1"), "publisher-key-untrusted");
        await RefuseAsync(() => withdrawn.ActivateAsync(selector, "test", "1", "test/v1"), "publisher-key-untrusted");
        Check(File.ReadAllText(selector).Trim() == "1", "Refused activation changed selector.");
        await MixedDiscoveryAsync(fixture, first, unknown);
        await IndependentPoliciesAsync(fixture, first, second, pin);
        LegacyCompatibility(first);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await catalog.ResolveAsync("test", "1", "test/v1", cancellationToken: cancelled.Token);
        }
        catch (OperationCanceledException) { return; }
        throw new Exception("Resolution ignored cancellation.");
    }

    private static async Task MixedDiscoveryAsync(PublisherFixture fixture, RSA first, RSA unknown)
    {
        fixture.Create("unsigned");
        fixture.Create("untrusted");
        fixture.Sign(unknown, "untrusted");
        InstalledPluginCatalog catalog = fixture.Catalog(Trust(first));
        foreach (var entries in new[] { catalog.ListAll(), await catalog.ListAllAsync("test/v1") })
        {
            Check(entries.Count == 3 && entries.Count(e => e.Installation is not null) == 1, "Discovery omitted a refusal.");
            Check(entries.Any(e => e.Refusal == "publisher-signature-missing") && entries.Any(e => e.Refusal == "publisher-key-untrusted"), "Discovery lost publisher codes.");
        }

        Refuse(() => catalog.List("test/v1"), "publisher-signature-missing");
        await RefuseAsync(() => catalog.ListAsync("test/v1"), "publisher-signature-missing");
    }

    private static async Task IndependentPoliciesAsync(PublisherFixture fixture, RSA first, RSA second, InstallationIdentity pin)
    {
        var tenantA = fixture.Catalog(Trust(first));
        var tenantB = fixture.Catalog(Trust(second));
        await Task.WhenAll(
            Task.Run(() => Check(tenantA.Resolve("test", "1", "test/v1", pin).Identity == pin, "Tenant A lost valid release.")),
            Task.Run(() => Refuse(() => tenantB.Resolve("test", "1", "test/v1", pin), "publisher-key-untrusted")));
        fixture.Create("partner");
        fixture.Sign(second, "partner");
        Check(tenantB.Resolve("partner", "1", "test/v1").Publisher is not null, "Refusal affected another publisher.");
    }

    private static void LegacyCompatibility(RSA key)
    {
        using var fixture = new PublisherFixture();
        fixture.Create();
        Check(fixture.Catalog(PluginPublisherPolicy.IntegrityOnly).Resolve("test", "1", "test/v1").Publisher is null, "Unsigned compatibility failed.");
        JsonNode manifest = JsonNode.Parse(File.ReadAllBytes(fixture.Manifest()))!;
        manifest["Schema"] = 1;
        manifest.AsObject().Remove("Runtimes");
        manifest.AsObject().Remove("ExternalFiles");
        using var runtime = File.OpenRead(Environment.ProcessPath!);
        manifest["RuntimeFiles"] = new JsonObject { ["dotnet"] = Convert.ToHexString(SHA256.HashData(runtime)) };
        File.WriteAllText(fixture.Manifest(), manifest.ToJsonString());
        fixture.Catalog(PluginPublisherPolicy.IntegrityOnly).Resolve("test", "1", "test/v1");
        Refuse(() => fixture.Catalog(Trust(key)).Resolve("test", "1", "test/v1"), "publisher-schema-unsupported");
        Refuse(() => fixture.Sign(key), "publisher-schema-unsupported");
    }

    private static async Task RefuseAsync(Func<Task> action, string code)
    {
        try
        {
            await action();
        }
        catch (PluginSignatureException error) when (error.ErrorCode == code) { return; }
        throw new Exception("Expected asynchronous publisher refusal: " + code);
    }
}
