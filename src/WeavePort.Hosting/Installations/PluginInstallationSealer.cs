using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using static WeavePort.Hosting.InstallationFiles;

namespace WeavePort.Hosting;
/// <summary>Creates deterministic schema-2 manifests for offline, owner-controlled plugin releases.</summary>
public static class PluginInstallationSealer
{
    private static readonly JsonSerializerOptions ManifestJson = new JsonSerializerOptions(InstalledPluginCatalog.ManifestJson)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    /// <summary>Validates and hashes a built release, then atomically replaces its manifest. Does not execute code or update selectors and pins.</summary>
    public static InstallationIdentity Seal(PluginSealOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.EntryPoints);
        ArgumentNullException.ThrowIfNull(options.RuntimeSources);
        ArgumentNullException.ThrowIfNull(options.StrictRuntimeFiles);
        ArgumentNullException.ThrowIfNull(options.ExternalFiles);
        ArgumentNullException.ThrowIfNull(options.Launch);
        ValidateSegment(options.Plugin);
        ValidateSegment(options.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Contract);
        string root = Path.GetFullPath(options.ReleaseDirectory);
        RejectLink(root);
        Dictionary<string, string> files = Inventory(root).Order(StringComparer.Ordinal).ToDictionary(p => p, p => Digest(BundlePath(root, p)), StringComparer.Ordinal);
        if (files.Count is < 1 or > InstallationLimits.Files || options.EntryPoints.Count is < 1 or > InstallationLimits.EntryPoints)
        {
            throw new InvalidDataException("Installation file or entry count exceeds limits.");
        }

        var entries = options.EntryPoints.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var runtimes = ReadRequirements(root, options, files, entries);
        PluginLaunchDeclaration launch = options.Launch.Freeze();
        launch.Validate(entries, entries);
        launch.ValidatePortable();
        CompatibilityDeclaration compatibility = InstallationCompatibility.Create(entries.Keys, launch);
        var external = options.ExternalFiles.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => Digest(p.Value), StringComparer.Ordinal);
        if (external.Keys.Intersect(entries.Keys, StringComparer.Ordinal).Any())
        {
            throw new InvalidDataException("External file aliases must not overlap runtime aliases.");
        }

        var manifest = new InstalledPluginCatalog.Manifest(2, options.Plugin, options.Version, options.Contract, entries, files, null, compatibility, launch, runtimes, external);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestJson);
        if (bytes.Length > InstallationLimits.ManifestBytes)
        {
            throw new InvalidDataException("Installation manifest exceeds 1 MiB.");
        }

        Write(root, bytes);
        return new(options.Plugin, options.Version, options.Contract, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static Dictionary<string, RuntimeDeclaration> ReadRequirements(string root, PluginSealOptions options, Dictionary<string, string> files, Dictionary<string, string> entries)
    {
        if (!options.RuntimeSources.Keys.All(entries.ContainsKey) || !options.StrictRuntimeFiles.Keys.All(entries.ContainsKey))
        {
            throw new InvalidDataException("Runtime source or strict hash names an undeclared entry.");
        }

        var runtimes = new Dictionary<string, RuntimeDeclaration>(StringComparer.Ordinal);
        foreach (var(alias, entry)in entries)
        {
            if (!files.ContainsKey(entry))
            {
                throw new InvalidDataException("Undeclared installation entry point.");
            }

            string source = options.RuntimeSources.TryGetValue(alias, out string? supplied) ? supplied : alias switch
            {
                "dotnet" => Path.ChangeExtension(entry, ".runtimeconfig.json"),
                "python" => "pyproject.toml",
                "node" => "package.json",
                _ => throw new InvalidDataException($"Unsupported runtime alias '{alias}'.")};
            if (!files.ContainsKey(source))
            {
                throw new InvalidDataException($"Missing runtime declaration '{source}'.");
            }

            string? digest = options.StrictRuntimeFiles.TryGetValue(alias, out string? runtime) ? Digest(runtime) : null;
            runtimes.Add(alias, RuntimeRequirements.Read(root, alias, entry, source, digest));
        }

        return runtimes;
    }

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void Write(string root, byte[] bytes)
    {
        string path = Path.Combine(root, "installation.json");
        if (File.Exists(path))
        {
            RejectLink(path);
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
