using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using WeavePort.Hosting;

internal sealed partial class DensityRun
{
    private async Task WriteReportAsync()
    {
        var total = DensityStats.Combine(_stats);
        double throughput = _seconds > 0 ? total.Success / _seconds : 0;
        bool passed = _failure is null && total.Errors == 0 && _stats.All(s => s.Success > 0 && s.Latency.Quantile(.99) <= _config.P99Ms) && total.Latency.Quantile(.99) <= _config.P99Ms &&
            _cleanup is { Workers: 0, Bindings: 0, Tenants: 0, MaintenanceFailure: null } && _after?.Failure is null;
        var report = new
        {
            passed,
            failure = _failure,
            config = _config,
            seconds = _seconds,
            registrationSeconds = _registrationSeconds,
            warmupSeconds = _warmupSeconds,
            throughput,
            offeredRate = _config.Traffic == "population" ? _config.Clients * _config.CallsPerCustomerPerMinute / 60 : _config.Rate,
            totals = total.Report(),
            tenants = _stats.Select(s => s.Report()),
            before = _before,
            after = _after,
            cleanup = _cleanup,
            resources = _resources?.Summary(),
            identity = new
            {
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtime = Environment.Version.ToString(),
                cpuCount = Environment.ProcessorCount,
                serverGc = GCSettings.IsServerGC,
                hostingSha256 = Hash(typeof(PluginHost).Assembly.Location),
                harnessSha256 = Hash(typeof(DensityRun).Assembly.Location),
                scope = "Source-built candidate, native trusted processes or local Docker stdio. Population and positive Rate use fixed intended arrivals; no M2 Ultra comparison."
            }
        };
        await File.WriteAllTextAsync(Path.Combine(_config.Output, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            passed,
            _config.Adapter,
            _config.Mode,
            _config.Clients,
            _config.Workers,
            rps = throughput,
            p99 = total.Latency.Quantile(.99),
            errors = total.Errors,
            failure = _failure,
            resources = _resources?.Summary()
        }));
        if (!passed)
        {
            Environment.ExitCode = 1;
        }
    }
}
