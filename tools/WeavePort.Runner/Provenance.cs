using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace WeavePort.Runner;

internal static class Provenance
{
    internal static async Task WriteAsync(string root, string output, string mode, TimeProvider clock)
    {
        var sources = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string[] folders = ["src", "samples", "plugins", "scripts", "tools", "benchmarks", "tests"];
        foreach (string path in folders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories)))
        {
            string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Split('/').Any(part => part is "bin" or "obj" or "__pycache__"))
            {
                continue;
            }

            sources[relative] = Hash(path);
        }
        foreach (string name in new[] { "global.json", "Directory.Build.props", "NuGet.Config", "WeavePort.slnx" })
        {
            sources[name] = Hash(Path.Combine(root, name));
        }

        var images = new Dictionary<string, string>();
        foreach (string image in new[] { "weaveport-poc-csharp:1", "weaveport-poc-python:1", "weaveport-poc-python:2", "weaveport-poc-typescript:1" })
        {
            images[image] = await CommandAsync(root, "docker", ["image", "inspect", image, "--format", "{{.Id}}"]);
        }

        string hostAssembly = Path.Combine(root, mode switch
        {
            "capacity" => "tests/WeavePort.CapacityTests/bin/Release/net10.0/WeavePort.Hosting.dll",
            "lifecycle" => "tests/WeavePort.LifecycleTests/bin/Release/net10.0/WeavePort.Hosting.dll",
            "load" => "tests/WeavePort.LoadTests/bin/Release/net10.0/WeavePort.Hosting.dll",
            "benchmark" => "benchmarks/WeavePort.Benchmarks/bin/Release/net10.0/WeavePort.Hosting.dll",
            _ => "tests/WeavePort.Docker.Tests/bin/Release/net10.0/WeavePort.Hosting.dll"
        });
        var metadata = new
        {
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            mode,
            generatedUtc = clock.GetUtcNow(),
            gitCommit = await CommandAsync(root, "git", ["rev-parse", "HEAD"]),
            dirty = (await CommandAsync(root, "git", ["status", "--porcelain"])).Length > 0,
            hostAssemblySha256 = Hash(hostAssembly),
            sources,
            packages = Directory.EnumerateFiles(Path.Combine(root, "artifacts/packages"), "*.nupkg").ToDictionary(p => Path.GetFileName(p), Hash),
            images,
            docker = JsonSerializer.Deserialize<JsonElement>(await CommandAsync(root, "docker", ["version", "--format", "{{json .}}"])),
            resources = JsonSerializer.Deserialize<JsonElement>(await CommandAsync(root, "docker", ["info", "--format", """{"cpus":{{.NCPU}},"memoryBytes":{{.MemTotal}}}"""])),
            topology = "one physical machine; Docker Desktop Linux VM; local workloads may contend",
            measurementMethod = MeasurementMethod(mode),
            safetyThresholds = new
            {
                unaffectedTenantFailures = 0,
                unaffectedTenantP99 = "max(100ms, 5*baselineP99)"
            }
        };
        await File.WriteAllTextAsync(Path.Combine(output, "provenance.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string MeasurementMethod(string mode) => mode switch
    {
        "benchmark" => "BenchmarkDotNet; configuration in exported results",
        "lifecycle" => "128-customer lifecycle transitions; injected idle age, real elapsed startup/cleanup and Docker/host memory snapshots",
        "capacity" => "closed-loop tenant concurrency; individual latency, throughput and periodic host/worker/VM resource samples",
        "load" => "individual invocation timings and periodic Docker resource samples",
        _ => "functional assertions and fault latency observations"
    };

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<string> CommandAsync(string root, string executable, string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info) ?? throw new IOException("Could not launch tool.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new IOException(executable + ": " + await error);
        }

        return (await output).Trim();
    }
}
