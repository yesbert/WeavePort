using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Hosting;

namespace WeavePort.CapacityTests;

// Trusted, explicitly launched diagnostic coordinator. Only it receives the Docker
// socket; DockerWorker never mounts the socket or this coordinator's output directory.
internal static partial class LinuxProbe
{
    internal static async Task RunAsync(string output)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux-only topology control");
        }

        Directory.CreateDirectory(output);
        var settings = LinuxProbeSettings.ReadEnvironment();
        var (languages, counts, seconds, workloads) = settings;
        var json = new JsonSerializerOptions { WriteIndented = true };
        using var diagnostics = RequestDiagnostics.Listen();
        TimeProvider clock = TimeProvider.System;
        var options = new WorkerPoolOptions(MaximumWorkers: counts[^1], MemoryBudgetMiB: counts[^1] * 256L, MaximumPristineWorkers: 0);
        await using var host = new PluginHost(options: options);
        await using var observer = new VmObserver(clock);
        await observer.StartAsync();
        var telemetry = new Telemetry(observer, clock);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        string transport = Environment.GetEnvironmentVariable("WEAVEPORT_TRANSPORT") ?? "cli";
        if (transport is not ("cli" or "socket"))
        {
            throw new ArgumentException("WEAVEPORT_TRANSPORT");
        }

        UnixSocketTransport? socketTransport = transport == "socket" ? new UnixSocketTransport("/ipc", Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET_DOCKER") ?? throw new ArgumentException("WEAVEPORT_SOCKET_DOCKER")) : null;
        if (socketTransport is not null)
        {
            File.SetUnixFileMode("/ipc", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var experiment = new LoadExperiment(host, clock, telemetry, cancel.Token, p => p with { SocketTransport = socketTransport });
        await WriteConfigurationAsync(output, settings, options, transport, socketTransport);
        await new Session(output, settings, experiment, telemetry, cancel).RunAsync();
    }

    private static async Task WriteConfigurationAsync(string output, LinuxProbeSettings settings,
        WorkerPoolOptions options, string transport, UnixSocketTransport? socketTransport)
    {
        var (languages, counts, seconds, workloads) = settings;
        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new
        {
            topology = "Same Mac and Docker Desktop VM; trusted .NET coordinator and CLI inside Linux VM; separate isolated workers",
            counts,
            seconds,
            languages,
            workloads,
            transport,
            socketTransport,
            payloadChars = RequestDiagnostics.PayloadChars,
            diagnostics = RequestDiagnostics.Enabled,
            framework = RuntimeInformation.FrameworkDescription,
            serverGc = System.Runtime.GCSettings.IsServerGC,
            processorCount = Environment.ProcessorCount,
            gcConfiguration = GC.GetConfigurationVariables(),
            os = RuntimeInformation.OSDescription,
            options,
            macMemoryTelemetry = "unavailable inside container; null, not zero",
            hostAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
            runnerAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(LinuxProbe).Assembly.Location))),
            docker = await Commands.RunAsync("docker", "version", "--format", "{{json .}}"),
            images = await Commands.RunAsync("docker", "image", "inspect", "weaveport-poc-csharp:1", "weaveport-poc-python:1", "weaveport-poc-typescript:1", "--format", "{{.Id}}")
        }, json));
    }
}
