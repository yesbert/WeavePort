using System.Text.Json;
using Chasm.SemanticVersioning.Ranges;
using Tomlyn;
using Tomlyn.Model;

namespace WeavePort.Hosting;
internal static class RuntimeRequirements
{
    private const int MaximumRequirementsBytes = 65536;
    internal static RuntimeDeclaration Read(string root, string alias, string entry, string source, string? digest = null)
    {
        string path = InstallationFiles.BundlePath(root, source);
        if (new FileInfo(path).Length > MaximumRequirementsBytes)
        {
            throw new InvalidDataException($"Runtime declaration '{source}' exceeds 64 KiB.");
        }

        try
        {
            string content = File.ReadAllText(path);
            string requirement = alias switch
            {
                "dotnet" => DotnetRequirements.Read(content),
                "python" => PythonRequirement(content),
                "node" => NodeRequirement(content),
                _ => throw new InvalidDataException($"Unsupported runtime ecosystem '{alias}'.")};
            if (alias == "dotnet" && source != Path.ChangeExtension(entry, ".runtimeconfig.json"))
            {
                throw new InvalidDataException("The .NET runtime declaration must be adjacent to its entry assembly.");
            }

            return new(alias, source, requirement, digest);
        }
        catch (Exception error) when (error is TomlException or InvalidDataException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Invalid runtime declaration '{source}': {error.Message}", error);
        }
    }

    private static string PythonRequirement(string content)
    {
        TomlTable table = Toml.ToModel(content);
        if (!table.TryGetValue("project", out var project) || project is not TomlTable metadata || !metadata.TryGetValue("requires-python", out var value) || value is not string requirement || metadata.TryGetValue("dynamic", out var dynamic) && dynamic is TomlArray array && array.Contains("requires-python"))
        {
            throw new InvalidDataException("pyproject.toml requires a static project.requires-python declaration.");
        }

        _ = PythonSpecifier.Parse(requirement);
        return requirement;
    }

    private static string NodeRequirement(string content)
    {
        InstallationFiles.RejectDuplicateFields(System.Text.Encoding.UTF8.GetBytes(content));
        using var json = JsonDocument.Parse(content);
        string requirement = json.RootElement.GetProperty("engines").GetProperty("node").GetString()!;
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        _ = VersionRange.Parse(requirement);
        return requirement;
    }
}
