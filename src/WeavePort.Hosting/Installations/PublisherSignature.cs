using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeavePort.Hosting;

internal static class PublisherSignature
{
    internal const string FileName = "installation.signature.json";
    internal const string Algorithm = "RSA-PSS-SHA256";
    internal const string Missing = "publisher-signature-missing";
    internal const string Malformed = "publisher-signature-malformed";
    internal const string Unsupported = "publisher-signature-unsupported";
    internal const string Untrusted = "publisher-key-untrusted";
    internal const string Invalid = "publisher-signature-invalid";
    internal const string SchemaUnsupported = "publisher-schema-unsupported";
    private const int MaximumEnvelopeBytes = 16 * 1024;
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("WeavePort.InstallationSignature.v1\n");

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    internal sealed record Envelope
    {
        public required int Schema { get; init; }
        public required string Algorithm { get; init; }
        public required string KeyId { get; init; }
        public required string Signature { get; init; }
    }

    internal static void ValidateKey(RSA key)
    {
        if (key.KeySize is not (3072 or 4096))
        {
            throw new ArgumentException("Publisher signing requires an RSA key of 3072 or 4096 bits.", nameof(key));
        }
    }

    internal static string KeyId(RSA key) => Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    internal static byte[] Message(byte[] manifest) => [.. Domain, .. manifest];

    internal static VerifiedPluginPublisher? Verify(string root, int schema, byte[] manifest, PluginPublisherPolicy policy)
    {
        if (!policy.RequiresSignature)
        {
            return null;
        }

        RequirePortable(schema);
        Envelope envelope = Read(root);
        TrustedPluginPublisher publisher = policy.Find(envelope.KeyId) ??
            throw new PluginSignatureException(Untrusted, "Release publisher key is not trusted by this host policy.");
        using RSA key = publisher.OpenKey();
        byte[] signature = Convert.FromBase64String(envelope.Signature);
        if (signature.Length != key.KeySize / 8 || !key.VerifyData(Message(manifest), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        {
            throw new PluginSignatureException(Invalid, "Publisher signature does not authenticate these manifest bytes.");
        }

        return new(publisher.Publisher, publisher.KeyId);
    }

    internal static void RequirePortable(int schema)
    {
        if (schema != 2)
        {
            throw new PluginSignatureException(SchemaUnsupported, "Publisher authentication requires schema 2; migrate and sign the release offline without automatically replacing persisted pins.");
        }
    }

    private static Envelope Read(string root)
    {
        string path = Path.Combine(root, FileName);
        try
        {
            InstallationFiles.RejectLink(path);
            if (Directory.Exists(path))
            {
                throw new InvalidDataException("Publisher signature must be a regular file.");
            }
            byte[] bytes = InstallationFiles.ReadBounded(path, MaximumEnvelopeBytes);
            InstallationFiles.RejectDuplicateFields(bytes);
            Envelope envelope = JsonSerializer.Deserialize(bytes, InstallationJsonContext.Default.Envelope) ??
                throw new InvalidDataException("Empty publisher signature envelope.");
            ValidateEnvelope(envelope);
            return envelope;
        }
        catch (FileNotFoundException error)
        {
            throw new PluginSignatureException(Missing, "Required publisher signature is missing.", error);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or FormatException)
        {
            throw new PluginSignatureException(Malformed, "Publisher signature envelope is malformed, linked or oversized.", error);
        }
    }

    private static void ValidateEnvelope(Envelope envelope)
    {
        if (envelope.Schema != 1 || envelope.Algorithm != Algorithm)
        {
            throw new PluginSignatureException(Unsupported, "Unsupported publisher signature format or algorithm.");
        }

        if (envelope.KeyId is null || envelope.KeyId.Length != 64 ||
            envelope.KeyId.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')) || envelope.Signature is null)
        {
            throw new InvalidDataException("Invalid publisher signature fields.");
        }

        byte[] signature = Convert.FromBase64String(envelope.Signature);
        if (signature.Length is not (384 or 512) || Convert.ToBase64String(signature) != envelope.Signature)
        {
            throw new InvalidDataException("Expected canonical base64 RSA signature bytes.");
        }
    }
}
