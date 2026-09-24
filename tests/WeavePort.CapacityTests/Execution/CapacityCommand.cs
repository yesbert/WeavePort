using System.Globalization;
using System.Text.Json;
using WeavePort.CapacityTests;
using WeavePort.Hosting;
using WeavePort.Runner;

namespace WeavePort.CapacityTests;

internal static class CapacityCommand
{
    internal static async Task RunAsync(CapacitySettings settings)
    {
        using var diagnostics = RequestDiagnostics.Listen();
        TimeProvider clock = TimeProvider.System;
        string output = Path.Combine(settings.Root, "reports/capacity", clock.GetUtcNow().ToString("yyyyMMdd-HHmmss-ffffff"));
        Directory.CreateDirectory(output);
        var capacityOptions = new WorkerPoolOptions(MaximumWorkers: 1024, MemoryBudgetMiB: 262144, MaximumPristineWorkers: 0);
        await Provenance.WriteAsync(settings.Root, output, "capacity", clock);
        if (settings.Languages == "diagnostic-python")
        {
            await File.WriteAllTextAsync(Path.Combine(output, "diagnostic-image.txt"), await Commands.RunAsync("docker", "image", "inspect", "weaveport-poc-diagnostic-python:1", "--format", "{{.Id}}"));
        }

        await WriteConfigurationAsync(settings, output, capacityOptions);
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        await using var observer = new VmObserver(clock);
        // Preserve the prior exploration envelope; VM/macOS guards remain authoritative for this opt-in test.
        await using var host = new PluginHost(options: capacityOptions);
        await observer.StartAsync();
        var telemetry = new Telemetry(observer, clock);
        var experiment = new LoadExperiment(host, clock, telemetry, cancel.Token);
        await new CapacitySession(settings, output, experiment, telemetry, cancel).RunAsync();
    }
    private static async Task WriteConfigurationAsync(CapacitySettings settings, string output, WorkerPoolOptions capacityOptions)
    {
        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new
        {
            counts = settings.Counts,
            seconds = settings.Seconds,
            languages = settings.Languages,
            workloads = settings.Workloads,
            diagnostics = RequestDiagnostics.Enabled,
            workerStatsEnabled = Telemetry.WorkerStatsEnabled,
            payloadChars = RequestDiagnostics.PayloadChars,
            workerPool = capacityOptions,
            model = "closed-loop; one outstanding request per tenant; same machine load generator and host",
            startupConcurrency = 8,
            guard = new
            {
                availableVmMemoryPercent = 20,
                hostRssGiB = 4,
                macMemoryFreePercent = 20,
                maxErrorPercent = 1,
                p99SmallMs = 500,
                p99PayloadMs = 2000
            },
            unrelatedRunningContainerCount = (await Commands.RunAsync("docker", "ps", "-q")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
        }, json));
    }
}
