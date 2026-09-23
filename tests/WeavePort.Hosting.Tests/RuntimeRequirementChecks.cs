using System.Text.Json;
using WeavePort.Hosting;

internal static class RuntimeRequirementChecks
{
    internal static void Run()
    {
        foreach (var (range, version, expected) in new (string, string, bool)[]
        {
            (">=3.11,<4", "3.12.1", true), (">=3.11,<4", "3.10.9", false),
            ("~=3.11.0", "3.12.0", false), ("~=3.11", "3.12.0", true),
            ("==3.11.*", "3.11.9", true), ("!=3.11.*", "3.11.9", false),
            (">=3.11", "3.12.0rc1", false), (">=3.12.0rc1", "3.12.0rc2", true),
            ("<3.12", "3.12rc1", false), (">3.11", "3.11.post1", false),
            ("==3.11", "3.11.0+local", true), ("==3.11+abc", "3.11+def", false),
            (">=1!3.11", "3.12", false), ("===3.11.0", "3.11.0", true),
            ("==3.11", "3.11.0", true), ("<=3.11", "3.11.0", true),
            (">=3.11.dev1", "3.11a1", true), (">=3.11rc1", "3.11", true)
        })
        {
            if (PythonSpecifier.Matches(range, version) != expected) throw new Exception($"Python mismatch {range} / {version}");
        }
        foreach (string bad in new[] { "", "3.11", ">=3.*", "~=3", "==3.11rc1.*", ">3.11+abc" })
        {
            try { PythonSpecifier.Parse(bad); throw new Exception("Invalid Python range accepted " + bad); }
            catch (Exception error) when (error is FormatException or ArgumentException) { }
        }
        Version[] versions = [new(10, 0, 1), new(10, 2, 0), new(10, 2, 3), new(10, 4, 5), new(11, 0, 6)];
        foreach (var (policy, expected) in new[] { ("Disable", (string?)null), ("LatestPatch", "10.0.1"), ("Minor", "10.0.1"), ("LatestMinor", "10.4.5"), ("Major", "10.0.1"), ("LatestMajor", "11.0.6") })
        {
            string? selected = DotnetRequirements.Select(new("Microsoft.NETCore.App", "10.0.0", policy), versions)?.ToString();
            if (selected != expected) throw new Exception($".NET selection {policy}: {selected} != {expected}");
        }
        if (DotnetRequirements.Select(new("core", "10.0.2", "LatestPatch"), [new(10, 0, 1)]) is not null) throw new Exception("Minimum patch bypass");
        if (DotnetRequirements.Select(new("core", "10.0.0", "Minor"), [new(11, 0, 0)]) is not null) throw new Exception("Major bypass");
        if (DotnetRequirements.Select(new("core", "10.0.0", "Major"), [new(11, 0, 0)]) is null) throw new Exception("Major denied");
        string multi = DotnetRequirements.Read("""{"runtimeOptions":{"frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.AspNetCore.App","version":"10.0.0"}]}}""");
        if (DotnetFrameworks.Matches(multi, "Microsoft.NETCore.App 10.0.12 [/nonexistent]")) throw new Exception("Missing framework accepted");
        foreach (string language in new[] { "python", "node" })
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", language + "-ranges.json")));
            foreach (var item in fixture.RootElement.EnumerateArray())
            {
                string range = item.GetProperty("range").GetString()!;
                string version = item.GetProperty("version").GetString()!;
                bool expected = item.GetProperty("expected").GetBoolean();
                bool actual = language == "python" ? PythonSpecifier.Matches(range, version) : Chasm.SemanticVersioning.Ranges.VersionRange.Parse(range).IsSatisfiedBy(Chasm.SemanticVersioning.SemanticVersion.Parse(version));
                if (actual != expected) throw new Exception($"{language} oracle mismatch: {range} / {version}: {actual} expected {expected}");
            }
        }
        Console.WriteLine("PASS ecosystem version and framework selection cases");
    }
}
