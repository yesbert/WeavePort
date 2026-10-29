using System.Security.Cryptography;
using System.Text.Json;

namespace WeavePort.Hosting;

/// <summary>Signs validated schema-2 manifests offline without changing their identity or executing plugin code.</summary>
public static class PluginInstallationSigner
{
    /// <summary>Atomically replaces only the detached signature. The caller owns the RSA private key and must keep the release immutable throughout signing.</summary>
    public static InstallationIdentity Sign(string releaseDirectory, string plugin, string version, string contract, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        PublisherSignature.ValidateKey(privateKey);
        string root = Path.GetFullPath(releaseDirectory);
        InstallationRelease release = InstalledPluginCatalog.ReadValidatedRelease(root, plugin, version, contract, PluginPublisherPolicy.IntegrityOnly, null);
        PublisherSignature.RequirePortable(release.Manifest.Schema);
        byte[] signature = privateKey.SignData(PublisherSignature.Message(release.Bytes), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var envelope = new PublisherSignature.Envelope
        {
            Schema = 1,
            Algorithm = PublisherSignature.Algorithm,
            KeyId = PublisherSignature.KeyId(privateKey),
            Signature = Convert.ToBase64String(signature)
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, InstallationJsonContext.Default.Envelope);
        Write(root, bytes);
        return release.Identity;
    }

    private static void Write(string root, byte[] bytes)
    {
        string path = Path.Combine(root, PublisherSignature.FileName);
        if (Path.Exists(path))
        {
            InstallationFiles.RejectLink(path);
        }

        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
