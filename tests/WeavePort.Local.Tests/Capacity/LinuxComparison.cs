using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Hosting;
using WeavePort.LocalTools;

// Measurement-only composition. Production profiles and their security policy stay unchanged.
internal sealed class LinuxComparison(string mode, string imagePrefix, string socketDirectory)
{
    internal ExecutionProfile Profile(LocalConfiguration config, string language)
    {
        return mode == "process"
            ? config.Profile(language, TimeSpan.FromSeconds(30))
            : new DockerProfile(imagePrefix + "-" + language + ":1", CpuCount: 24,
                Timeout: TimeSpan.FromSeconds(30), SocketTransport: new UnixSocketTransport("/ipc", socketDirectory));
    }

    internal static async Task<int> RunAsync(LocalConfiguration config, string output)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("This comparison requires Linux");
        }

        string mode = Environment.GetEnvironmentVariable("WEAVEPORT_COMPARE_MODE") ?? "process";
        if (mode is not ("process" or "container"))
        {
            throw new ArgumentException("Unknown comparison mode");
        }

        if (!config.UseUnixSocket || config.SocketBufferBytes is not null)
        {
            throw new ArgumentException("Use OS-default Unix socket buffers for both comparison modes");
        }

        string prefix = Environment.GetEnvironmentVariable("WEAVEPORT_COMPARE_IMAGE") ?? "weaveport-platform";
        string sockets = Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET_DOCKER") ?? throw new ArgumentException("Missing private socket directory");
        Directory.CreateDirectory("/ipc");
        File.SetUnixFileMode("/ipc", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        await File.WriteAllTextAsync(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(new
        {
            mode,
            topology = "Linux Docker Desktop VM; same trusted coordinator container; child processes versus separate worker containers; NOT bare metal",
            os = RuntimeInformation.OSDescription,
            runtime = Environment.Version.ToString(),
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            hostingSha256 = Hash(typeof(PluginHost).Assembly.Location),
            runnerSha256 = Hash(typeof(LinuxComparison).Assembly.Location),
            csharpSha256 = Hash(config.Csharp),
            pythonSha256 = Hash(config.PythonScript),
            typescriptSha256 = Hash(config.TypeScriptScript),
            workerPolicy = mode == "process" ? "trusted children sharing coordinator cgroup; no per-worker quotas" : "256 MiB, 24 CPU ceiling, 64 PIDs, read-only, nonroot, no network",
            resourceScope = "Common sampler: coordinator root RSS/CPU and VM MemAvailable. Per-worker sampling omitted in BOTH modes to avoid different observer cost. Coordinator cgroup includes children only in process mode.",
            images = await LocalConfiguration.VersionAsync("/usr/local/bin/docker", "image", "inspect", prefix + "-csharp:1", prefix + "-python:1", prefix + "-typescript:1", "--format", "{{.Id}}")
        }, new JsonSerializerOptions { WriteIndented = true }));
        return await LocalCapacity.RunAsync(config, output, new LinuxComparison(mode, prefix, sockets));
    }
}
