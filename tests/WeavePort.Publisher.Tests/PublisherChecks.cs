using System.Security.Cryptography;
using System.Text.Json.Nodes;
using WeavePort.Hosting;
using static PublisherFixture;

internal static class PublisherChecks
{
    internal static void Run(RSA first, RSA second, RSA unknown)
    {
        using var fixture = new PublisherFixture();
        InstallationIdentity pin = fixture.Create();
        byte[] manifest = File.ReadAllBytes(fixture.Manifest());
        var trust = Trust(first);
        Refuse(() => fixture.Verify(trust), "publisher-signature-missing");
        Check(fixture.Sign(first) == pin, "Signing changed pin.");
        Check(fixture.Verify(trust, pin: pin).Publisher?.KeyId == new TrustedPluginPublisher("first", first.ExportSubjectPublicKeyInfo()).KeyId, "Wrong verified publisher.");
        Check(manifest.SequenceEqual(File.ReadAllBytes(fixture.Manifest())), "Signing changed manifest bytes.");
        Refuse(() => fixture.Verify(Trust(unknown)), "publisher-key-untrusted");
        Invalid(() => PluginInstallationSealer.Seal(fixture.Options()));
        Check(manifest.SequenceEqual(File.ReadAllBytes(fixture.Manifest())), "Refused resealing changed manifest.");
        EnvelopeChecks(fixture, trust);
        ContentChecks(fixture, trust, first);
        RotationChecks(fixture, first, second, pin);
        ConfigurationChecks(first);
        ReservedPaths(first);
    }

    private static void EnvelopeChecks(PublisherFixture fixture, PluginPublisherPolicy trust)
    {
        string original = File.ReadAllText(fixture.Signature());
        Mutate(fixture, trust, "{}", "publisher-signature-malformed");
        Mutate(fixture, trust, original.Replace("\"Schema\":1", "\"Schema\":1,\"Schema\":1"), "publisher-signature-malformed");
        Mutate(fixture, trust, original.Replace("\"Schema\":1", "\"Schema\":2"), "publisher-signature-unsupported");
        Mutate(fixture, trust, original.Replace("RSA-PSS-SHA256", "none"), "publisher-signature-unsupported");
        Mutate(fixture, trust, original.Insert(1, "\"PublicKey\":\"untrusted\","), "publisher-signature-malformed");
        Mutate(fixture, trust, new string(' ', 16385), "publisher-signature-malformed");
        JsonNode envelope = JsonNode.Parse(original)!;
        envelope["Signature"] = "bad base64!";
        Mutate(fixture, trust, envelope.ToJsonString(), "publisher-signature-malformed");
        envelope["Signature"] = Convert.ToBase64String(new byte[384]);
        Mutate(fixture, trust, envelope.ToJsonString(), "publisher-signature-invalid");
        envelope["Signature"] = JsonNode.Parse(original)!["Signature"]!.GetValue<string>() + "\n";
        Mutate(fixture, trust, envelope.ToJsonString(), "publisher-signature-malformed");
        File.WriteAllText(fixture.Signature(), original);
        if (!OperatingSystem.IsWindows())
        {
            LinkCheck(fixture, trust, original);
        }
    }

    private static void LinkCheck(PublisherFixture fixture, PluginPublisherPolicy trust, string original)
    {
        string outside = Path.Combine(fixture.Root, "outside.json");
        File.WriteAllText(outside, original);
        File.Delete(fixture.Signature());
        File.CreateSymbolicLink(fixture.Signature(), outside);
        Refuse(() => fixture.Verify(trust), "publisher-signature-malformed");
        File.Delete(fixture.Signature());
        File.WriteAllText(fixture.Signature(), original);
    }

    private static void Mutate(PublisherFixture fixture, PluginPublisherPolicy trust, string value, string code)
    {
        File.WriteAllText(fixture.Signature(), value);
        Refuse(() => fixture.Verify(trust), code);
    }

    private static void ContentChecks(PublisherFixture fixture, PluginPublisherPolicy trust, RSA key)
    {
        byte[] manifest = File.ReadAllBytes(fixture.Manifest());
        byte[] signature = File.ReadAllBytes(fixture.Signature());
        File.AppendAllText(fixture.Manifest(), " ");
        Refuse(() => fixture.Verify(trust), "publisher-signature-invalid");
        File.WriteAllBytes(fixture.Manifest(), manifest);
        string worker = Path.Combine(fixture.Release(), "worker.dll");
        string original = File.ReadAllText(worker);
        File.WriteAllText(worker, "altered");
        Invalid(() => fixture.Verify(trust));
        Invalid(() => fixture.Sign(key));
        Check(signature.SequenceEqual(File.ReadAllBytes(fixture.Signature())), "Failed signing replaced signature.");
        File.WriteAllText(worker, original);
        string extra = Path.Combine(fixture.Release(), "extra.dll");
        File.WriteAllText(extra, "undeclared");
        Invalid(() => fixture.Verify(trust));
        File.Delete(extra);
        JsonNode forged = JsonNode.Parse(manifest)!;
        forged["Version"] = "2";
        File.WriteAllText(fixture.Manifest(), forged.ToJsonString());
        Refuse(() => fixture.Verify(trust), "publisher-signature-invalid");
        File.WriteAllBytes(fixture.Manifest(), manifest);
    }

    private static void RotationChecks(PublisherFixture fixture, RSA first, RSA second, InstallationIdentity pin)
    {
        var overlap = PluginPublisherPolicy.RequireTrustedPublisher([
            new("first", first.ExportSubjectPublicKeyInfo()), new("second", second.ExportSubjectPublicKeyInfo())]);
        Check(fixture.Verify(overlap, pin: pin).Publisher?.Publisher == "first", "First publisher missing.");
        Check(fixture.Sign(second) == pin, "Rotation changed identity.");
        Check(fixture.Verify(overlap, pin: pin).Publisher?.Publisher == "second", "Second publisher missing.");
        Check(fixture.Verify(Trust(second), pin: pin).Identity == pin, "Withdrawn old key affected new signature.");
        Refuse(() => fixture.Verify(Trust(first), pin: pin), "publisher-key-untrusted");
        Check(fixture.Verify(PluginPublisherPolicy.IntegrityOnly).Publisher is null, "Integrity-only claimed publisher authenticity.");
    }

    private static void ConfigurationChecks(RSA first)
    {
        byte[] der = first.ExportSubjectPublicKeyInfo();
        var publisher = new TrustedPluginPublisher("configured", der);
        Array.Clear(der);
        TrustedPluginPublisher[] entries = [publisher];
        var policy = PluginPublisherPolicy.RequireTrustedPublisher(entries);
        entries[0] = null!;
        using var fixture = new PublisherFixture();
        fixture.Create();
        fixture.Sign(first);
        Check(fixture.Verify(policy).Publisher?.Publisher == "configured", "Trust snapshot retained caller-owned mutable data.");
        ArgumentFailure(() => PluginPublisherPolicy.RequireTrustedPublisher([]));
        ArgumentFailure(() => PluginPublisherPolicy.RequireTrustedPublisher([publisher, publisher]));
        using RSA small = RSA.Create(2048);
        ArgumentFailure(() => new TrustedPluginPublisher("weak", small.ExportSubjectPublicKeyInfo()));
        ArgumentFailure(() => fixture.Sign(small));
        ArgumentFailure(() => new TrustedPluginPublisher("trailing", [.. first.ExportSubjectPublicKeyInfo(), 0]));
    }

    private static void ArgumentFailure(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException) { return; }
        throw new Exception("Expected invalid trust configuration.");
    }

    private static void ReservedPaths(RSA key)
    {
        using var fixture = new PublisherFixture();
        fixture.Create();
        string nested = Directory.CreateDirectory(Path.Combine(fixture.Release(), "nested")).FullName;
        string file = Path.Combine(nested, "installation.signature.json");
        File.WriteAllText(file, "ordinary bundle content");
        PluginInstallationSealer.Seal(fixture.Options());
        fixture.Sign(key);
        fixture.Verify(Trust(key));
        File.WriteAllText(file, "changed");
        Invalid(() => fixture.Verify(Trust(key)));
        File.WriteAllText(file, "ordinary bundle content");
        JsonNode manifest = JsonNode.Parse(File.ReadAllBytes(fixture.Manifest()))!;
        manifest["Files"]!["installation.signature.json"] = new string('0', 64);
        File.WriteAllText(fixture.Manifest(), manifest.ToJsonString());
        Invalid(() => fixture.Sign(key));
    }
}
