using System.Diagnostics;
using System.Text.RegularExpressions;

internal sealed partial class DensityResources
{
    // Includes unbound pristine/starting workers, which no customer session exposes yet.
    private static async Task<string[]> ReadDockerInstancesAsync(IEnumerable<int> ownedPids)
    {
        var info = new ProcessStartInfo("/bin/ps") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-ww", "-p", string.Join(',', ownedPids), "-o", "command=" }) info.ArgumentList.Add(arg);
        using var ps = Process.Start(info)!;
        string commands = await ps.StandardOutput.ReadToEndAsync();
        await ps.WaitForExitAsync();
        if (ps.ExitCode != 0) throw new IOException("Owned Docker process observer failed");
        return ExtractDockerInstances(commands);
    }

    internal static string[] ExtractDockerInstances(string commands) => Regex.Matches(commands,
        @"(?:^|\s)--name(?:=|\s+)(weaveport-[a-f0-9]{32})(?=\s|$)").Select(m => m.Groups[1].Value).Distinct().ToArray();
}
