using System.Security.Cryptography;
using static WeavePort.Hosting.InstallationFiles;

namespace WeavePort.Hosting;

public sealed partial class InstalledPluginCatalog
{
    internal static InstallationRelease ReadValidatedRelease(string root, string plugin, string version, string contract,
        PluginPublisherPolicy policy, InstallationIdentity? pinned)
    {
        ValidateSegment(plugin);
        ValidateSegment(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        root = Path.GetFullPath(root);
        (Manifest manifest, byte[] bytes) = ReadManifest(root, Path.Combine(root, ManifestFileName));
        VerifiedPluginPublisher? publisher = PublisherSignature.Verify(root, manifest.Schema, bytes, policy);
        var identity = new InstallationIdentity(plugin, version, contract, Convert.ToHexString(SHA256.HashData(bytes)));
        ValidateManifestDeclarations(manifest, identity, pinned);
        ValidateContent(root, manifest);
        return new(manifest, bytes, identity, publisher);
    }

    private static void ValidateContent(string root, Manifest manifest)
    {
        if (manifest.Files.ContainsKey(PublisherSignature.FileName) || manifest.EntryPoints.ContainsValue(PublisherSignature.FileName))
        {
            throw new InvalidDataException("The root publisher signature is reserved metadata, not bundle content or an entry point.");
        }

        InstallationCompatibility.Validate(manifest.Compatibility, manifest.EntryPoints.Keys);
        if (!manifest.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Inventory(root)))
        {
            throw new InvalidDataException("Installation bundle file set changed.");
        }

        foreach (var file in manifest.Files)
        {
            VerifyFile(BundlePath(root, file.Key), file.Value);
        }

        var entries = ResolveEntryPoints(root, manifest);
        manifest.Launch?.Validate(entries, entries);
        if (manifest.Launch is { } launch && manifest.Compatibility!.Protocol != (launch.Ownership.Contains(WorkerReusePolicy.Shared) ? 2 : 1))
        {
            throw new InvalidDataException("Launch ownership does not match its declared wire protocol.");
        }

        if (manifest.Schema == 2)
        {
            ValidatePortableDeclarations(root, manifest);
        }
    }

    private static void ValidatePortableDeclarations(string root, Manifest manifest)
    {
        if (manifest.RuntimeFiles is not null || manifest.Runtimes is null || manifest.ExternalFiles is null ||
            !manifest.Runtimes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(manifest.EntryPoints.Keys) ||
            manifest.ExternalFiles.Keys.Intersect(manifest.Runtimes.Keys, StringComparer.Ordinal).Any())
        {
            throw new InvalidDataException("Invalid schema-2 runtime policy.");
        }

        manifest.Launch?.ValidatePortable();
        foreach (var (alias, declaration) in manifest.Runtimes)
        {
            if (declaration is null || string.IsNullOrWhiteSpace(declaration.Source) ||
                string.IsNullOrWhiteSpace(declaration.Requirement) || !manifest.Files.ContainsKey(declaration.Source) ||
                declaration.Ecosystem != alias ||
                RuntimeRequirements.Read(root, alias, manifest.EntryPoints[alias], declaration.Source, declaration.Sha256) != declaration)
            {
                throw new InvalidDataException($"Runtime '{alias}' declaration disagrees with its bundle source.");
            }
        }
    }
}
