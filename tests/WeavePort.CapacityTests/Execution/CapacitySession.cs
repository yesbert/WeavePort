using System.Globalization;
using System.Text.Json;
using WeavePort.CapacityTests;
using WeavePort.Hosting;
using WeavePort.Runner;

namespace WeavePort.CapacityTests;

internal sealed class CapacitySession(CapacitySettings settings, string output, LoadExperiment experiment,
    Telemetry telemetry, CancellationTokenSource cancel)
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly List<LoadResult> _results = [];
    private string _reason = "configured maximum tenant count reached";
    private LoadResult? _recovery;
    private int _exitCode;
    internal async Task RunAsync()
    {
        telemetry.Start();
        try
        {
            await Task.Delay(3000, cancel.Token);
            foreach (int count in settings.Counts)
            {
                if (telemetry.StopReason is { } before)
                {
                    _reason = before;
                    break;
                }
                string? stop = await RunLevelAsync(count);
                if (stop is not null)
                {
                    _reason = stop;
                    break;
                }
            }

        }
        catch (Exception error)
        {
            _reason = error.GetType().Name + ": " + error.Message.Replace(settings.Root, ".", StringComparison.Ordinal);
            _exitCode = cancel.IsCancellationRequested ? 130 : 1;
            Console.WriteLine(_reason);
        }
        int createdTenants = experiment.Tenants.Count;
        await File.WriteAllTextAsync(Path.Combine(output, "startup.json"), JsonSerializer.Serialize(experiment.Tenants.Select(t => new { t.Index, t.Language, t.StartupMs, t.Session.Instance }), _json));

        try
        {
            await RecoverAsync();
        }
        catch (Exception error)
        {
            _reason += "; recovery unavailable: " + error.GetType().Name + ": " + error.Message.Replace(settings.Root, ".", StringComparison.Ordinal);
            _exitCode = cancel.IsCancellationRequested ? 130 : 1;
        }
        finally
        {
            await CleanupAndReportAsync(createdTenants);
        }
        Console.WriteLine("Stop reason: " + _reason);
        Console.WriteLine("Evidence directory: " + Path.GetRelativePath(settings.Root, output));
        Environment.ExitCode = _exitCode;
    }
    private async Task CleanupAndReportAsync(int createdTenants)
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
            _reason += "; cleanup failed: " + error.GetType().Name;
            _exitCode = 1;
        }
        await telemetry.DisposeAsync();
        if (telemetry.StopReason?.Contains("telemetry", StringComparison.OrdinalIgnoreCase) == true)
        {
            _exitCode = 1;
            _reason += "; " + telemetry.StopReason;
        }
        await File.WriteAllTextAsync(Path.Combine(output, "resources.json"), JsonSerializer.Serialize(telemetry.Samples, _json));
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            stopReason = _reason,
            telemetryStopReason = telemetry.StopReason,
            exitCode = _exitCode,
            createdTenants,
            stages = _results.Select(r => r with { Raw = [] }),
            recovery = _recovery
        }, _json));
        await File.WriteAllTextAsync(Path.Combine(output, "exit-code.txt"), _exitCode.ToString());
    }
    private async Task<string?> RunLevelAsync(int count)
    {
        telemetry.Phase(count + ":startup");
        Console.WriteLine("Starting tenant stage " + count);
        await experiment.GrowAsync(count, settings.Languages);
        await experiment.PrepareAsync();
        await telemetry.WatchAsync(experiment.WorkerNames);
        if (settings.Languages == "diagnostic-python")
        {
            await File.WriteAllTextAsync(Path.Combine(output, count + "-calibration.json"), JsonSerializer.Serialize(await experiment.CalibrateAsync(), _json));
        }

        telemetry.Phase(count + ":idle");
        await Task.Delay(3000, cancel.Token);
        string? stop = await RunWorkloadsAsync(count);
        await File.WriteAllTextAsync(Path.Combine(output, "startup.json"), JsonSerializer.Serialize(experiment.Tenants.Select(t => new { t.Index, t.Language, t.StartupMs, t.Session.Instance }), _json));
        return stop;
    }
    private async Task<string?> RunWorkloadsAsync(int count)
    {
        foreach (string workload in settings.Workloads)
        {
            if (telemetry.StopReason is { } guard)
            {
                return guard;
            }
            LoadResult result = await experiment.RunAsync(workload, settings.Seconds);
            _results.Add(result);
            await File.WriteAllTextAsync(Path.Combine(output, count + "-" + workload + ".json"), JsonSerializer.Serialize(result, _json));
            if (telemetry.StopReason is { } pressure)
            {
                return pressure;
            }
            if (result.ExceedsQualityBoundary)
            {
                return "service-quality exploration boundary at " + count + " tenants / " + workload;
            }

        }
        return null;
    }
    private async Task RecoverAsync()
    {
        if (cancel.IsCancellationRequested || experiment.Tenants.Count == 0)
        {
            return;
        }

        await telemetry.WatchAsync(() => []);
        telemetry.Phase("recovery:cleanup");
        await experiment.ShrinkAsync(1);
        await experiment.PrepareAsync();
        await telemetry.WatchAsync(experiment.WorkerNames);
        await Task.Delay(2000, cancel.Token);
        _recovery = await experiment.RunAsync("echo", settings.Seconds, recovery: true);
        if (_recovery.Errors != 0 || _recovery.P99Ms > 100)
        {
            _exitCode = 1;
            _reason += "; recovery failed";
        }
    }
}
