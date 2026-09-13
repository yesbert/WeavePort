using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Hosting;

namespace WeavePort.CapacityTests;

// Trusted, explicitly launched diagnostic coordinator. Only it receives the Docker
// socket; DockerWorker never mounts the socket or this coordinator's output directory.
internal static class LinuxProbe
{
    internal static async Task RunAsync(string output)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux-only topology control");
        Directory.CreateDirectory(output);
        string languages = Environment.GetEnvironmentVariable("WEAVEPORT_LANGUAGES") ?? "mixed";
        if (languages is not ("mixed" or "csharp" or "python" or "typescript" or "diagnostic-python")) throw new ArgumentException("WEAVEPORT_LANGUAGES");
        int[] counts = (Environment.GetEnvironmentVariable("WEAVEPORT_COUNTS") ?? "1,64,128").Split(',').Select(int.Parse).ToArray();
        if (counts.Length == 0 || counts[0] < 1 || counts[^1] > 384 || !counts.SequenceEqual(counts.Distinct().Order())) throw new ArgumentException("WEAVEPORT_COUNTS");
        int seconds = RequestDiagnostics.ReadInt("WEAVEPORT_SECONDS", 20, 2, 120);
        string[] workloads = (Environment.GetEnvironmentVariable("WEAVEPORT_WORKLOADS") ?? "echo,payload").Split(',');
        if (workloads.Any(w => w is not ("echo" or "payload" or "delay"))) throw new ArgumentException("WEAVEPORT_WORKLOADS");
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
        if (transport is not ("cli" or "socket")) throw new ArgumentException("WEAVEPORT_TRANSPORT");
        UnixSocketTransport? socketTransport = transport == "socket" ? new UnixSocketTransport("/ipc", Environment.GetEnvironmentVariable("WEAVEPORT_SOCKET_DOCKER") ?? throw new ArgumentException("WEAVEPORT_SOCKET_DOCKER")) : null;
        if (socketTransport is not null) File.SetUnixFileMode("/ipc", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var experiment = new LoadExperiment(host, clock, telemetry, cancel.Token, p => p with { SocketTransport = socketTransport });
        await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new
        {
            topology = "Same Mac and Docker Desktop VM; trusted .NET coordinator and CLI inside Linux VM; separate isolated workers",
            counts, seconds, languages, workloads, transport, socketTransport, payloadChars = RequestDiagnostics.PayloadChars,
            diagnostics = RequestDiagnostics.Enabled, framework = RuntimeInformation.FrameworkDescription,
            serverGc = System.Runtime.GCSettings.IsServerGC, processorCount = Environment.ProcessorCount,
            gcConfiguration = GC.GetConfigurationVariables(),
            os = RuntimeInformation.OSDescription, options, macMemoryTelemetry = "unavailable inside container; null, not zero",
            hostAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
            runnerAssemblySha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(LinuxProbe).Assembly.Location))),
            docker = await Commands.RunAsync("docker", "version", "--format", "{{json .}}"),
            images = await Commands.RunAsync("docker", "image", "inspect", "weaveport-poc-csharp:1", "weaveport-poc-python:1", "weaveport-poc-typescript:1", "--format", "{{.Id}}")
        }, json));
        telemetry.Start();
        int exitCode = 0;
        string reason = "configured maximum reached";
        try
        {
            foreach (int count in counts)
            {
                telemetry.Phase(count + ":startup");
                await experiment.GrowAsync(count, languages);
                await experiment.PrepareAsync();
                await telemetry.WatchAsync(experiment.WorkerNames);
                if (languages == "diagnostic-python") await File.WriteAllTextAsync(Path.Combine(output, count + "-calibration.json"), JsonSerializer.Serialize(await experiment.CalibrateAsync(), json));
                await Task.Delay(3000, cancel.Token);
                bool stop = false;
                foreach (string workload in workloads)
                {
                    LoadResult result = await experiment.RunAsync(workload, seconds);
                    await File.WriteAllTextAsync(Path.Combine(output, count + "-" + workload + ".json"), JsonSerializer.Serialize(result, json));
                    if (telemetry.StopReason is not null || result.ExceedsQualityBoundary)
                    {
                        reason = telemetry.StopReason ?? "service-quality exploration boundary";
                        stop = true;
                        break;
                    }
                }
                if (stop) break;
            }
            await telemetry.WatchAsync(() => []);
            await experiment.ShrinkAsync(1);
            await experiment.PrepareAsync();
            await telemetry.WatchAsync(experiment.WorkerNames);
            LoadResult recovery = await experiment.RunAsync("echo", seconds, recovery: true);
            await File.WriteAllTextAsync(Path.Combine(output, "recovery.json"), JsonSerializer.Serialize(recovery, json));
            if (recovery.Errors != 0 || recovery.P99Ms > 100) throw new IOException("Recovery failed");
        }
        catch (Exception error)
        {
            exitCode = 1;
            reason = error.GetType().Name + ": " + error.Message;
            Console.WriteLine(reason);
        }
        finally
        {
            await telemetry.WatchAsync(() => []);
            await experiment.ShrinkAsync(0);
            await telemetry.DisposeAsync();
            if (telemetry.StopReason is not null) { exitCode = 1; reason += "; " + telemetry.StopReason; }
            await File.WriteAllTextAsync(Path.Combine(output, "resources.json"), JsonSerializer.Serialize(telemetry.Samples, json));
            await File.WriteAllTextAsync(Path.Combine(output, "stop-reason.txt"), reason);
            await File.WriteAllTextAsync(Path.Combine(output, "exit-code.txt"), exitCode.ToString());
        }
        Environment.ExitCode = exitCode;
    }
}
