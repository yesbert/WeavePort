using System.Security.Cryptography;

namespace WeavePort.Hosting;

/// <summary>An immutable host-owned publisher label and RSA public key. Trust applies to every plugin in its catalog.</summary>
public sealed class TrustedPluginPublisher
{
    private readonly byte[] _publicKey;
    /// <summary>Gets the publisher label supplied by trusted host configuration.</summary>
    public string Publisher { get; }
    /// <summary>Gets the lowercase SHA-256 fingerprint of canonical DER SubjectPublicKeyInfo.</summary>
    public string KeyId { get; }

    /// <summary>Copies and validates a canonical DER RSA public key of 3072 or 4096 bits. No private key is retained.</summary>
    public TrustedPluginPublisher(string publisher, byte[] subjectPublicKeyInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        using RSA key = RSA.Create();
        key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int consumed);
        PublisherSignature.ValidateKey(key);
        _publicKey = key.ExportSubjectPublicKeyInfo();
        if (consumed != subjectPublicKeyInfo.Length || !_publicKey.AsSpan().SequenceEqual(subjectPublicKeyInfo))
        {
            throw new ArgumentException("Expected one canonical DER SubjectPublicKeyInfo public key.", nameof(subjectPublicKeyInfo));
        }

        Publisher = publisher;
        KeyId = PublisherSignature.KeyId(key);
    }

    internal RSA OpenKey()
    {
        RSA key = RSA.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
