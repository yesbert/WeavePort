using System.Globalization;
using System.Text.Json;
using WeavePort.CapacityTests;
using WeavePort.Hosting;
using WeavePort.Runner;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
if (args.Length > 0 && args[0] == "--diagnostic-report") { await DiagnosticReport.WriteAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--linux-report") { await LinuxReport.WriteAsync(args[1]); return; }
if (args.Length > 0 && args[0] == "--linux-probe") { await LinuxProbe.RunAsync(args[1]); return; }
if (args.SequenceEqual(new[] { "--self-test" })) { InstrumentationChecks.Run(); return; }
if (args.SequenceEqual(new[] { "--restart-test" })) { await InstrumentationChecks.RunRestartAsync(); return; }
string root = Path.GetFullPath(args[0]);
int[] counts = (args.Length > 1 ? args[1] : "1,8,32,64,128,256").Split(',').Select(int.Parse).ToArray();
int seconds = args.Length > 2 ? int.Parse(args[2]) : 10;
string languages = args.Length > 3 ? args[3] : "mixed";
string[] workloads = (args.Length > 4 ? args[4] : "echo,delay,payload").Split(',');
if (counts.Length == 0 || counts[0] < 1 || counts[^1] > 1024 || !counts.SequenceEqual(counts.Distinct().Order()) || seconds < 2 || seconds > 120 ||
    languages is not ("mixed" or "csharp" or "python" or "typescript" or "diagnostic-python") || workloads.Any(w => w is not ("echo" or "delay" or "payload")))
    throw new ArgumentException("Expected increasing counts 1..1024, duration 2..120 seconds, mixed|csharp|python|typescript|diagnostic-python, and echo,delay,payload workloads");
using var diagnostics = RequestDiagnostics.Listen();
TimeProvider clock = TimeProvider.System;
string output = Path.Combine(root, "reports/capacity", clock.GetUtcNow().ToString("yyyyMMdd-HHmmss-ffffff"));
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions { WriteIndented = true };
var capacityOptions = new WorkerPoolOptions(MaximumWorkers: 1024, MemoryBudgetMiB: 262144, MaximumPristineWorkers: 0);
await Provenance.WriteAsync(root, output, "capacity", clock);
if (languages == "diagnostic-python")
    await File.WriteAllTextAsync(Path.Combine(output, "diagnostic-image.txt"), await Commands.RunAsync("docker", "image", "inspect", "weaveport-poc-diagnostic-python:1", "--format", "{{.Id}}"));
await File.WriteAllTextAsync(Path.Combine(output, "configuration.json"), JsonSerializer.Serialize(new { counts, seconds, languages, workloads, diagnostics = RequestDiagnostics.Enabled, workerStatsEnabled = Telemetry.WorkerStatsEnabled, payloadChars = RequestDiagnostics.PayloadChars, workerPool = capacityOptions,
    model = "closed-loop; one outstanding request per tenant; same machine load generator and host", startupConcurrency = 8,
    guard = new { availableVmMemoryPercent = 20, hostRssGiB = 4, macMemoryFreePercent = 20, maxErrorPercent = 1, p99SmallMs = 500, p99PayloadMs = 2000 },
    unrelatedRunningContainerCount = (await Commands.RunAsync("docker", "ps", "-q")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length }, json));
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
await using var observer = new VmObserver(clock);
// Preserve the prior exploration envelope; VM/macOS guards remain authoritative for this opt-in test.
await using var host = new PluginHost(options: capacityOptions);
await observer.StartAsync();
var telemetry = new Telemetry(observer, clock);
var experiment = new LoadExperiment(host, clock, telemetry, cancel.Token);
var results = new List<LoadResult>();
string reason = "configured maximum tenant count reached";
LoadResult? recovery = null;
int exitCode = 0;
telemetry.Start();
try
{
    await Task.Delay(3000, cancel.Token);
    foreach (int count in counts)
    {
        if (telemetry.StopReason is { } before) { reason = before; break; }
        telemetry.Phase(count + ":startup");
        Console.WriteLine("Starting tenant stage " + count);
        await experiment.GrowAsync(count, languages);
        await experiment.PrepareAsync();
        await telemetry.WatchAsync(experiment.WorkerNames);
        if (languages == "diagnostic-python")
            await File.WriteAllTextAsync(Path.Combine(output, count + "-calibration.json"), JsonSerializer.Serialize(await experiment.CalibrateAsync(), json));
        telemetry.Phase(count + ":idle");
        await Task.Delay(3000, cancel.Token);
        bool stop = false;
        foreach (string workload in workloads)
        {
            if (telemetry.StopReason is { } guard) { reason = guard; stop = true; break; }
            LoadResult result = await experiment.RunAsync(workload, seconds);
            results.Add(result);
            await File.WriteAllTextAsync(Path.Combine(output, count + "-" + workload + ".json"), JsonSerializer.Serialize(result, json));
            if (telemetry.StopReason is { } pressure) { reason = pressure; stop = true; }
            else if (result.ExceedsQualityBoundary)
            { reason = "service-quality exploration boundary at " + count + " tenants / " + workload; stop = true; }
            if (stop) break;
        }
        await File.WriteAllTextAsync(Path.Combine(output, "startup.json"), JsonSerializer.Serialize(experiment.Tenants.Select(t => new { t.Index, t.Language, t.StartupMs, t.Session.Instance }), json));
        if (stop) break;
    }

}
catch (Exception error)
{
    reason = error.GetType().Name + ": " + error.Message.Replace(root, ".", StringComparison.Ordinal);
    exitCode = cancel.IsCancellationRequested ? 130 : 1;
    Console.WriteLine(reason);
}
int createdTenants = experiment.Tenants.Count;
await File.WriteAllTextAsync(Path.Combine(output, "startup.json"), JsonSerializer.Serialize(experiment.Tenants.Select(t => new { t.Index, t.Language, t.StartupMs, t.Session.Instance }), json));

try
{
    if (!cancel.IsCancellationRequested && experiment.Tenants.Count > 0)
    {
        await telemetry.WatchAsync(() => []);
        telemetry.Phase("recovery:cleanup");
        await experiment.ShrinkAsync(1);
        await experiment.PrepareAsync();
        await telemetry.WatchAsync(experiment.WorkerNames);
        await Task.Delay(2000, cancel.Token);
        recovery = await experiment.RunAsync("echo", seconds, recovery: true);
        if (recovery.Errors != 0 || recovery.P99Ms > 100) { exitCode = 1; reason += "; recovery failed"; }
    }
}
catch (Exception error)
{
    reason += "; recovery unavailable: " + error.GetType().Name + ": " + error.Message.Replace(root, ".", StringComparison.Ordinal);
    exitCode = cancel.IsCancellationRequested ? 130 : 1;
}
finally
{
    telemetry.Phase("final:cleanup");
    try
    {
        await telemetry.WatchAsync(() => []);
        await experiment.ShrinkAsync(0);
        await Task.Delay(1000);
    }
    catch (Exception error)
    {
        reason += "; cleanup failed: " + error.GetType().Name;
        exitCode = 1;
    }
    await telemetry.DisposeAsync();
    if (telemetry.StopReason?.Contains("telemetry", StringComparison.OrdinalIgnoreCase) == true) { exitCode = 1; reason += "; " + telemetry.StopReason; }
    await File.WriteAllTextAsync(Path.Combine(output, "resources.json"), JsonSerializer.Serialize(telemetry.Samples, json));
    await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { stopReason = reason, telemetryStopReason = telemetry.StopReason, exitCode,
        createdTenants, stages = results.Select(r => r with { Raw = [] }), recovery }, json));
    await File.WriteAllTextAsync(Path.Combine(output, "exit-code.txt"), exitCode.ToString());
}
Console.WriteLine("Stop reason: " + reason);
Console.WriteLine("Evidence directory: " + Path.GetRelativePath(root, output));
Environment.ExitCode = exitCode;
