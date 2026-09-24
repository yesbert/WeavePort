using System.Text.Json;

namespace WeavePort.Hosting;
internal static class DotnetRequirements
{
    private static readonly string[] Policies = ["Disable", "LatestPatch", "Minor", "LatestMinor", "Major", "LatestMajor"];
    internal static string Read(string content)
    {
        InstallationFiles.RejectDuplicateFields(System.Text.Encoding.UTF8.GetBytes(content));
        using var json = JsonDocument.Parse(content);
        JsonElement options = json.RootElement.GetProperty("runtimeOptions");
        RejectLegacy(options);
        string policy = Policy(options, "Minor");
        if (options.TryGetProperty("includedFrameworks", out _) || options.TryGetProperty("framework", out _) == options.TryGetProperty("frameworks", out _))
        {
            throw new InvalidDataException("Portable .NET requires framework-dependent runtime configuration.");
        }

        JsonElement[] frameworks = options.TryGetProperty("framework", out var single) ? [single] : options.GetProperty("frameworks").EnumerateArray().ToArray();
        FrameworkRequirement[] requirements = frameworks.Select(f => ReadFramework(f, policy)).OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
        if (requirements.Length is < 1 or > 32 || requirements.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() != requirements.Length)
        {
            throw new InvalidDataException("Invalid or repeated .NET framework requirements.");
        }

        return JsonSerializer.Serialize(requirements);
    }

    private static FrameworkRequirement ReadFramework(JsonElement element, string fallback)
    {
        RejectLegacy(element);
        string name = element.GetProperty("name").GetString()!;
        InstallationFiles.ValidateSegment(name);
        string version = element.GetProperty("version").GetString()!;
        _ = ParseVersion(version);
        return new(name, version, Policy(element, fallback));
    }

    private static void RejectLegacy(JsonElement element)
    {
        if (element.TryGetProperty("applyPatches", out _) || element.TryGetProperty("rollForwardOnNoCandidateFx", out _))
        {
            throw new InvalidDataException("Legacy .NET selection settings are unsupported in portable declarations; use rollForward.");
        }
    }

    private static string Policy(JsonElement element, string fallback)
    {
        string policy = element.TryGetProperty("rollForward", out var value) ? value.GetString()! : fallback;
        if (!Policies.Contains(policy, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Unsupported .NET rollForward policy.");
        }

        return policy;
    }

    internal static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0 || value.Any(c => !char.IsAsciiDigit(c) && c != '.'))
        {
            throw new FormatException("Expected a stable three-component .NET framework version.");
        }

        return version;
    }

    internal static Version? Select(FrameworkRequirement requirement, IEnumerable<Version> available)
    {
        _ = ParseVersion(requirement.Version);
        Version[] candidates = available.Where(v => Allows(requirement, v)).Order().ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        return requirement.RollForward switch
        {
            "Disable" => candidates[0],
            "LatestPatch" or "LatestMinor" or "LatestMajor" => candidates[^1],
            _ => candidates.Last(v => v.Major == candidates[0].Major && v.Minor == candidates[0].Minor)};
    }

    internal static bool Allows(FrameworkRequirement requirement, Version candidate)
    {
        Version minimum = ParseVersion(requirement.Version);
        if (candidate < minimum)
        {
            return false;
        }

        return requirement.RollForward switch
        {
            "Disable" => candidate == minimum,
            "LatestPatch" => candidate.Major == minimum.Major && candidate.Minor == minimum.Minor,
            "Minor" or "LatestMinor" => candidate.Major == minimum.Major,
            "Major" or "LatestMajor" => true,
            _ => false
        };
    }
}
