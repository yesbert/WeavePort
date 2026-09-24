using static WeavePort.Hosting.InstallationFiles;

namespace WeavePort.Hosting;
public sealed partial class InstalledPluginCatalog
{
    private Dictionary<string, string> SelectRuntimes(string root, Manifest manifest)
    {
        if (manifest.Schema == 1)
        {
            return SelectLegacyRuntimes(manifest);
        }

        if (manifest.RuntimeFiles is not null || manifest.Runtimes is null || manifest.ExternalFiles is null || !manifest.Runtimes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(manifest.EntryPoints.Keys) || manifest.ExternalFiles.Keys.Intersect(manifest.Runtimes.Keys, StringComparer.Ordinal).Any())
        {
            throw new InvalidDataException("Invalid schema-2 runtime policy.");
        }

        manifest.Launch?.ValidatePortable();
        VerifyExternal(manifest.ExternalFiles);
        var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var(alias, declaration)in manifest.Runtimes)
        {
            if (declaration is null || string.IsNullOrWhiteSpace(declaration.Source) || string.IsNullOrWhiteSpace(declaration.Requirement) || !manifest.Files.ContainsKey(declaration.Source) || declaration.Ecosystem != alias || RuntimeRequirements.Read(root, alias, manifest.EntryPoints[alias], declaration.Source, declaration.Sha256) != declaration)
            {
                throw new InvalidDataException($"Runtime '{alias}' declaration disagrees with its bundle source.");
            }

            if (!runtimeFiles.TryGetValue(alias, out string? executable) || !Path.IsPathFullyQualified(executable))
            {
                throw new InvalidDataException($"Runtime '{alias}' is not approved at an absolute executable path.");
            }

            selected.Add(alias, executable);
        }

        return selected;
    }

    private void VerifyExternal(IReadOnlyDictionary<string, string> files)
    {
        foreach (var(alias, digest)in files)
        {
            if (!runtimeFiles.TryGetValue(alias, out string? path))
            {
                throw new InvalidDataException($"Unapproved installation file alias '{alias}'.");
            }

            VerifyFile(path, digest);
        }
    }

    /// <summary>Resolves an exact release using bounded, cancellable runtime probes.</summary>
    public async Task<InstalledPlugin> ResolveAsync(string plugin, string version, string contract, InstallationIdentity? pinned = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InstalledPlugin result = ResolveContent(plugin, version, contract, pinned);
        if (result.RuntimeValidation is { } validation)
        {
            await validation.ValidateAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>Lists selected releases by contract using cancellable runtime probes.</summary>
    public async Task<IReadOnlyList<InstalledPlugin>> ListAsync(string contract, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contract);
        var result = new List<InstalledPlugin>();
        if (!Directory.Exists(releases))
        {
            return result;
        }

        foreach (string directory in Directory.EnumerateDirectories(releases).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(directory);
            string selector = Path.Combine(directory, "active.txt");
            if (!File.Exists(selector))
            {
                continue;
            }

            string version = ReadSelection(selector);
            string root = Path.Combine(directory, "releases", version);
            (Manifest manifest, _) = ReadManifest(root, Path.Combine(root, "installation.json"));
            if (manifest.Contract != contract)
            {
                continue;
            }

            result.Add(await ResolveAsync(Path.GetFileName(directory), version, contract, cancellationToken: cancellationToken));
        }

        return result.AsReadOnly();
    }

    /// <summary>Validates with cancellable probes before atomically replacing a selector.</summary>
    public async Task ActivateAsync(string selector, string plugin, string version, string contract, CancellationToken cancellationToken = default)
    {
        _ = await ResolveAsync(plugin, version, contract, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        WriteSelection(selector, version);
    }

    private Dictionary<string, string> SelectLegacyRuntimes(Manifest manifest)
    {
        if (manifest.RuntimeFiles is null || manifest.Runtimes is not null || manifest.ExternalFiles is not null)
        {
            throw new InvalidDataException("Invalid schema-1 runtime policy.");
        }

        VerifyExternal(manifest.RuntimeFiles);
        return manifest.RuntimeFiles.Keys.ToDictionary(key => key, key => Path.GetFullPath(runtimeFiles[key]), StringComparer.Ordinal);
    }
}
