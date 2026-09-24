using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Hosting;

namespace WeavePort.CapacityTests;

internal static partial class LinuxProbe
{
    private sealed class Session(string output, LinuxProbeSettings settings, LoadExperiment experiment,
        Telemetry telemetry, CancellationTokenSource cancel)
    {
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private int _exitCode;
        private string _reason = "configured maximum reached";
        internal async Task RunAsync()
        {
            telemetry.Start();
            try
            {
                foreach (int count in settings.Counts)
                {
                    string? stop = await RunLevelAsync(count);
                    if (stop is not null)
                    {
                        _reason = stop;
                        break;
                    }
                }
                await RecoverAsync();
            }
            catch (Exception error)
            {
                _exitCode = 1;
                _reason = error.GetType().Name + ": " + error.Message;
                Console.WriteLine(_reason);
            }
            finally
            {
                await telemetry.WatchAsync(() => []);
                await experiment.ShrinkAsync(0);
                await telemetry.DisposeAsync();
                if (telemetry.StopReason is not null)
                {
                    _exitCode = 1;
                    _reason += "; " + telemetry.StopReason;
                }
                await File.WriteAllTextAsync(Path.Combine(output, "resources.json"), JsonSerializer.Serialize(telemetry.Samples, _json));
                await File.WriteAllTextAsync(Path.Combine(output, "stop-reason.txt"), _reason);
                await File.WriteAllTextAsync(Path.Combine(output, "exit-code.txt"), _exitCode.ToString());
            }
            Environment.ExitCode = _exitCode;
        }
        private async Task<string?> RunLevelAsync(int count)
        {
            telemetry.Phase(count + ":startup");
            await experiment.GrowAsync(count, settings.Languages);
            await experiment.PrepareAsync();
            await telemetry.WatchAsync(experiment.WorkerNames);
            if (settings.Languages == "diagnostic-python")
            {
                await File.WriteAllTextAsync(Path.Combine(output, count + "-calibration.json"), JsonSerializer.Serialize(await experiment.CalibrateAsync(), _json));
            }

            await Task.Delay(3000, cancel.Token);
            return await RunWorkloadsAsync(count);
        }
        private async Task<string?> RunWorkloadsAsync(int count)
        {
            foreach (string workload in settings.Workloads)
            {
                LoadResult result = await experiment.RunAsync(workload, settings.Seconds);
                await File.WriteAllTextAsync(Path.Combine(output, count + "-" + workload + ".json"), JsonSerializer.Serialize(result, _json));
                if (telemetry.StopReason is not null || result.ExceedsQualityBoundary)
                {
                    return telemetry.StopReason ?? "service-quality exploration boundary";
                }
            }
            return null;
        }
        private async Task RecoverAsync()
        {
            await telemetry.WatchAsync(() => []);
            await experiment.ShrinkAsync(1);
            await experiment.PrepareAsync();
            await telemetry.WatchAsync(experiment.WorkerNames);
            LoadResult recovery = await experiment.RunAsync("echo", settings.Seconds, recovery: true);
            await File.WriteAllTextAsync(Path.Combine(output, "recovery.json"), JsonSerializer.Serialize(recovery, _json));
            if (recovery.Errors != 0 || recovery.P99Ms > 100)
            {
                throw new IOException("Recovery failed");
            }
        }
    }
}
