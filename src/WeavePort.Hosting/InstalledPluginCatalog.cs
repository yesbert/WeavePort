using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace WeavePort.Hosting;
/// <summary>Persistable identity of a trusted installation manifest, separate from domain state.</summary>
public sealed record InstallationIdentity(string Plugin, string Version, string Contract, string Digest);
/// <summary>A validated local installation. Deployment files must remain unchanged during execution.</summary>
public sealed class InstalledPlugin
{
    /// <summary>Gets the identity to persist with recoverable application state.</summary>
    public InstallationIdentity Identity { get; }
    /// <summary>Gets the resolved, verified entry points by trusted alias.</summary>
    public IReadOnlyDictionary<string, string> EntryPoints { get; }

    internal InstalledPlugin(InstallationIdentity identity, Dictionary<string, string> entries)
    {
        Identity = identity;
        EntryPoints = new ReadOnlyDictionary<string, string>(entries);
    }
}

/// <summary>Validates trusted local release manifests; does not install code or provide an OS sandbox.</summary>
public sealed class InstalledPluginCatalog(string releases, IReadOnlyDictionary<string, string> runtimeFiles)
{
    private sealed record Manifest(int Schema, string Plugin, string Version, string Contract, Dictionary<string, string> EntryPoints, Dictionary<string, string> Files, Dictionary<string, string> RuntimeFiles)
    {
        public CompatibilityDeclaration? Compatibility { get; init; }
    }

    /// <summary>Resolves an exact release and optionally requires a previously persisted manifest identity.</summary>
    public InstalledPlugin Resolve(string plugin, string version, string contract, InstallationIdentity? pinned = null)
    {
        ValidateSegment(version);
        string root = Path.GetFullPath(Path.Combine(releases, version));
        string path = Path.Combine(root, "installation.json");
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
        {
            throw new InvalidDataException("Installation manifest missing or oversized.");
        }

        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Linked installation roots/manifests are unsupported.");
        }

        byte[] bytes = File.ReadAllBytes(path);
        Manifest manifest;
        try
        {
            RejectDuplicateFields(bytes);
            manifest = JsonSerializer.Deserialize<Manifest>(bytes) ?? throw new InvalidDataException("Empty installation.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Malformed installation.", error);
        }

        var identity = new InstallationIdentity(plugin, version, contract, Convert.ToHexString(SHA256.HashData(bytes)));
        if (manifest.Schema != 1 || manifest.Plugin != plugin || manifest.Version != version || manifest.Contract != contract || (pinned is not null && identity != pinned) || manifest.Files is null || manifest.EntryPoints is null || manifest.RuntimeFiles is null || manifest.Files.Count is < 1 or > 4096 || manifest.EntryPoints.Count is < 1 or > 32 || !manifest.RuntimeFiles.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(runtimeFiles.Keys))
        {
            throw new InvalidDataException("Installation identity, schema, contract or runtime selection mismatch.");
        }

        InstallationCompatibility.Validate(manifest.Compatibility, manifest.EntryPoints.Keys);
        var actualFiles = Inventory(root);
        if (!manifest.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(actualFiles))
        {
            throw new InvalidDataException("Installation bundle file set changed.");
        }

        foreach (var file in manifest.Files)
        {
            VerifyFile(BundlePath(root, file.Key), file.Value);
        }

        foreach (var runtime in manifest.RuntimeFiles)
        {
            VerifyFile(runtimeFiles[runtime.Key], runtime.Value);
        }

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in manifest.EntryPoints)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || !manifest.Files.ContainsKey(entry.Value))
            {
                throw new InvalidDataException("Undeclared installation entry point.");
            }

            entries.Add(entry.Key, BundlePath(root, entry.Value));
        }

        return new InstalledPlugin(identity, entries);
    }

    /// <summary>Reads a trusted default selector. This is only for new logical operations.</summary>
    public static string ReadSelection(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 128)
        {
            throw new InvalidDataException("Invalid installation selector.");
        }

        string version = File.ReadAllText(path).Trim();
        ValidateSegment(version);
        return version;
    }

    /// <summary>Validates a release then replaces the default selector without mutating existing pins.</summary>
    public void Activate(string selector, string plugin, string version, string contract)
    {
        _ = Resolve(plugin, version, contract);
        string path = Path.GetFullPath(selector);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, version + "\n");
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

    private static void ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value is "." or ".." || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
        {
            throw new InvalidDataException("Invalid installation release identifier.");
        }
    }

    private static string BundlePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Split('/').Any(p => p is "" or "." or ".."))
        {
            throw new InvalidDataException("Invalid bundle path.");
        }

        string current = root;
        foreach (string part in relative.Split('/'))
        {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                throw new InvalidDataException("Missing bundle file.");
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Bundle links are unsupported.");
            }
        }

        return current;
    }

    private static HashSet<string> Inventory(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        int entries = 0;
        while (pending.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 8192)
                {
                    throw new InvalidDataException("Installation inventory exceeds limit.");
                }

                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Bundle links are unsupported.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(path);
                }
                else if (path != Path.Combine(root, "installation.json"))
                {
                    files.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }

        return files;
    }

    private static void VerifyFile(string path, string digest)
    {
        if (!File.Exists(path) || digest is null || digest.Length != 64)
        {
            throw new InvalidDataException("Missing installation file or digest.");
        }

        using var input = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(input)) != digest)
        {
            throw new InvalidDataException("Installation content changed.");
        }
    }

    private static void RejectDuplicateFields(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        Check(document.RootElement);
        static void Check(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate installation field.");
                }

                Check(property.Value);
            }
        }
    }
}
