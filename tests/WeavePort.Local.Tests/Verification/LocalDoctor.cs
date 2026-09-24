using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.LocalTools;
using WeavePort.Testing;

internal static class LocalDoctor
{
    internal static async Task<int> RunAsync(LocalConfiguration config, string output)
    {
        var checks = new List<object>();
        int failures = 0;
        foreach ((string name, string executable) in new[] { ("python", config.Python), ("node", config.Node) })
        {
            failures += await CheckRuntimeAsync(checks, name, executable);
        }
        foreach (string path in new[] { config.Csharp, config.PythonScript, config.TypeScriptScript })
        {
            failures += CheckFile(checks, path);
        }
        failures += await CheckFrameworkAsync(checks, config);
        await File.WriteAllTextAsync(Path.Combine(output, "doctor.json"), JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            hostRuntime = Environment.Version.ToString(),
            config.SelfContained,
            config.UseUnixSocket,
            config.SocketBufferBytes,
            dockerRequired = false,
            failures,
            checks
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Doctor: {failures} failures; Docker not required; trusted processes only.");
        return failures == 0 ? 0 : 1;
    }
    private static async Task<int> CheckRuntimeAsync(List<object> checks, string name, string executable)
    {
        int failures = 0;
        try
        {
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            {
                throw new FileNotFoundException();
            }

            string version = await LocalConfiguration.VersionAsync(executable, "--version");
            Version number = Version.Parse(version.Replace("Python ", "", StringComparison.Ordinal).TrimStart('v'));
            bool supported = name == "python" ? number.Major == 3 && number.Minor >= 14 : number.Major > 24 || number.Major == 24 && number.Minor >= 12;
            if (!supported)
            {
                throw new NotSupportedException("Runtime version below fixture prerequisite");
            }

            checks.Add(new
            {
                name,
                passed = true,
                version
            });
        }
        catch (Exception error) { failures++; checks.Add(new { name, passed = false, error = error.GetType().Name }); }
        return failures;
    }

    private static int CheckFile(List<object> checks, string path)
    {
        bool exists = File.Exists(path);
        checks.Add(new
        {
            name = Path.GetFileName(path),
            passed = exists
        });
        return exists ? 0 : 1;
    }

    private static async Task<int> CheckFrameworkAsync(List<object> checks, LocalConfiguration config)
    {
        if (config.SelfContained)
        {
            return 0;
        }
        int failures = 0;
        try
        {
            string runtimes = await LocalConfiguration.VersionAsync(config.Dotnet, "--list-runtimes");
            if (!runtimes.Contains("Microsoft.NETCore.App 10.", StringComparison.Ordinal))
            {
                throw new NotSupportedException();
            }

            checks.Add(new
            {
                name = ".NET 10 runtime",
                passed = true
            });
        }
        catch (Exception error) { failures++; checks.Add(new { name = ".NET 10 runtime", passed = false, error = error.GetType().Name }); }
        return failures;
    }

}
