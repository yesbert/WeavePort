using System.Security.Cryptography;
using System.Text.Json;

namespace WeavePort.Hosting;

internal static class InstallationFiles
{
    internal const string ManifestFileName = "installation.json";
    internal const string SelectorFileName = "active.txt";
    internal const string ReleasesDirectoryName = "releases";

    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Linked installation paths are unsupported.");
        }
    }

    internal static void ValidateSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > InstallationLimits.ReleaseIdentifierCharacters ||
            value is "." or ".." ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new InvalidDataException("Invalid installation release identifier.");
        }
    }

    internal static string BundlePath(string root, string relative)
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

    internal static HashSet<string> Inventory(string root)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        int entries = 0;
        while (pending.TryPop(out string? directory))
        {
            CollectDirectory(root, directory, files, pending, ref entries);
        }

        return files;
    }

    private static void CollectDirectory(string root, string directory, HashSet<string> files, Stack<string> pending, ref int entries)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (++entries > InstallationLimits.InventoryEntries)
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
                continue;
            }

            if (path == Path.Combine(root, ManifestFileName))
            {
                continue;
            }

            files.Add(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    internal static void VerifyFile(string path, string digest)
    {
        if (!File.Exists(path) || digest is null || digest.Length != SHA256.HashSizeInBytes * 2)
        {
            throw new InvalidDataException("Missing installation file or digest.");
        }

        using var input = File.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(input)) != digest)
        {
            throw new InvalidDataException("Installation content changed.");
        }
    }

    internal static void RejectDuplicateFields(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = InstallationLimits.JsonDepth });
        ValidateUniqueFieldNames(document.RootElement);
    }

    private static void ValidateUniqueFieldNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            ValidateArrayFieldNames(element);
            return;
        }

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

            ValidateUniqueFieldNames(property.Value);
        }
    }

    private static void ValidateArrayFieldNames(JsonElement element)
    {
        foreach (var item in element.EnumerateArray())
        {
            ValidateUniqueFieldNames(item);
        }
    }
}
