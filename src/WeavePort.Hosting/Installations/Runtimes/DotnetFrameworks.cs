using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeavePort.Hosting;
internal static class DotnetFrameworks
{
    private const int MaximumRuntimeConfigBytes = 65536;
    private sealed record Installed(string Name, Version Version, string Directory);
    internal static bool Matches(string requirement, string output)
    {
        List<Installed> available = ReadInventory(output);
        var constraints = JsonSerializer.Deserialize<FrameworkRequirement[]>(requirement)!.ToList();
        var expanded = new HashSet<(string, Version)>();
        for (int iteration = 0; iteration < 128; iteration++)
        {
            var expansion = ExpandConstraints(available, constraints, expanded);
            if (!expansion.Compatible)
            {
                return false;
            }

            if (!expansion.Added)
            {
                return true;
            }
        }

        throw new InvalidDataException("Installed framework dependency resolution exceeds limit.");
    }

    private static (bool Compatible, bool Added) ExpandConstraints(List<Installed> available, List<FrameworkRequirement> constraints, HashSet<(string, Version)> expanded)
    {
        bool added = false;
        // GroupBy buffers this iteration's constraints before the first group is yielded.
        foreach (var group in constraints.GroupBy(f => f.Name, StringComparer.Ordinal))
        {
            Installed[] installed = available.Where(f => f.Name == group.Key).ToArray();
            var versions = installed.Select(f => f.Version).Where(v => group.All(r => DotnetRequirements.Allows(r, v))).ToArray();
            Version? selected = group.Select(r => DotnetRequirements.Select(r, versions)).Max();
            if (selected is null)
            {
                return (false, added);
            }

            if (!expanded.Add((group.Key, selected)))
            {
                continue;
            }

            FrameworkRequirement[] dependencies = ReadDependencies(installed.First(f => f.Version == selected));
            constraints.AddRange(dependencies);
            added |= dependencies.Length > 0;
        }

        return (true, added);
    }

    private static List<Installed> ReadInventory(string output)
    {
        var available = new List<Installed>();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = Regex.Match(line, @"^(\S+) ([0-9]+\.[0-9]+\.[0-9]+)(?:-[^ ]+)? \[(.+)\]$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success)
            {
                throw new FormatException("Malformed .NET runtime inventory.");
            }

            if (line.Split(' ')[1].Contains('-'))
            {
                continue;
            }

            available.Add(new(match.Groups[1].Value, DotnetRequirements.ParseVersion(match.Groups[2].Value), match.Groups[3].Value));
        }

        return available;
    }

    private static FrameworkRequirement[] ReadDependencies(Installed framework)
    {
        string path = Path.Combine(framework.Directory, framework.Version.ToString(), framework.Name + ".runtimeconfig.json");
        if (!File.Exists(path))
        {
            return[];
        }

        if (new FileInfo(path).Length > MaximumRuntimeConfigBytes)
        {
            throw new InvalidDataException("Installed framework configuration exceeds limit.");
        }

        string content = File.ReadAllText(path);
        using var config = JsonDocument.Parse(content);
        JsonElement options = config.RootElement.GetProperty("runtimeOptions");
        return options.TryGetProperty("framework", out _) || options.TryGetProperty("frameworks", out _) ? JsonSerializer.Deserialize<FrameworkRequirement[]>(DotnetRequirements.Read(content))! : [];
    }
}
