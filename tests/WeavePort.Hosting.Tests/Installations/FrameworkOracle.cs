using System.Text.Json;
using WeavePort.Hosting;

internal static class FrameworkOracle
{
    internal static void Run(string path)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in cases.RootElement.EnumerateArray())
        {
            string requirement = DotnetRequirements.Read(item.GetProperty("config").GetString()!);
            bool actual = DotnetFrameworks.Matches(requirement, item.GetProperty("inventory").GetString()!);
            if (actual != item.GetProperty("expected").GetBoolean())
            {
                throw new Exception("Framework oracle mismatch " + requirement);
            }
        }
        Console.WriteLine($"PASS {cases.RootElement.GetArrayLength()} real dotnet framework resolution comparisons");
    }
}
