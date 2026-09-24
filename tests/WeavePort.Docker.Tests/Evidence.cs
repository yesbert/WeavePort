using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WeavePort.Testing;

internal sealed class Evidence(string directory)
{
    private readonly List<object> _checks = [];
    private readonly List<object> _measurements = [];
    private readonly List<string> _lines = [];
    internal string DirectoryPath => directory;
    internal int Failures { get; private set; }

    internal async Task CheckAsync(string name, Func<Task> action)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            await action();
            _checks.Add(new
            {
                name,
                passed = true,
                elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds
            });
            _lines.Add($"- PASS: {name}");
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            Failures++;
            _checks.Add(new
            {
                name,
                passed = false,
                error = error.Message
            });
            _lines.Add($"- FAIL: {name}: {error.Message}");
            Console.WriteLine($"FAIL {name}: {error.Message}");
        }
        await SaveAsync();
    }

    internal void Measure(string name, double[] values, object details)
    {
        double p50 = ContractChecks.Percentile(values, 50);
        double p95 = ContractChecks.Percentile(values, 95);
        double p99 = ContractChecks.Percentile(values, 99);
        _measurements.Add(new
        {
            name,
            samplesMs = values,
            p50,
            p95,
            p99,
            details
        });
        _lines.Add($"- {name}: n={values.Length}, p50={p50:F3} ms, p95={p95:F3} ms, p99={p99:F3} ms");
        Console.WriteLine($"MEASURE {name}: p99={p99:F3} ms");
    }

    internal async Task FinishAsync()
    {
        await SaveAsync();
        string report = "# WeavePort functional verification\n\n" + $"Failures: {Failures}. Generated {DateTimeOffset.UtcNow:O}.\n\n" +
            "Topology: one physical machine with Docker workers; actual coordinator OS/runtime is recorded in results.json. These are local worker experiments, not a separate-machine scaling claim.\n\n" +
            "The host process and Docker engine are shared. Container boundaries do not prove protection against kernel exploits or host failure.\n\n" +
            "Raw fault-latency observations and environment: results.json. Safety thresholds were recorded before execution. Performance benchmarks run separately through BenchmarkDotNet.\n\n" + string.Join("\n", _lines) + "\n";
        await File.WriteAllTextAsync(Path.Combine(directory, "report.md"), report);
    }

    private Task SaveAsync()
    {
        using Process current = Process.GetCurrentProcess();
        return File.WriteAllTextAsync(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new
        {
            environment = new
            {
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                dotnet = Environment.Version.ToString(),
                processors = Environment.ProcessorCount,
                serverGc = System.Runtime.GCSettings.IsServerGC,
                gcConfiguration = GC.GetConfigurationVariables(),
                hostWorkingSetBytes = current.WorkingSet64,
                hostCpuTimeMs = current.TotalProcessorTime.TotalMilliseconds,
                topology = "single-machine-docker-desktop"
            },
            failures = Failures,
            checks = _checks,
            measurements = _measurements
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

}
