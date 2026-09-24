using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;
using WeavePort.LoadTests;

internal sealed class LoadExperiment(string output)
{
    private readonly List<object> _measurements = [];
    private readonly List<string> _lines = new() { "# Per-request latency and sampled container resources", "", "These are request-level observations, separate from BenchmarkDotNet iteration statistics. Resource samples are periodic observations, not an exact peak-memory measurement.", "" };
    private const int WarmupRequests = 20;
    private const int MeasuredRequests = 300;
    private const double SearchP99LimitMs = 100;
    private int _failures;
    internal async Task RunAsync()
    {
        Directory.CreateDirectory(output);
        await using var host = new PluginHost();
        foreach (string language in new[] { "csharp", "python", "typescript" })
        {
            await MeasureLanguageAsync(host, language);
        }
        await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            failures = _failures,
            measurements = _measurements,
            scope = "sequential per-request latency; one local machine; failed calls remain in raw samples"
        }, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(output, "report.md"), string.Join(Environment.NewLine, _lines));
        Environment.ExitCode = _failures == 0 ? 0 : 1;

    }
    private async Task MeasureLanguageAsync(PluginHost host, string language)
    {
        await using IPluginSession session = await host.BindAsync(new PluginContext("load-" + language, "demo", "1", "default", JsonSerializer.SerializeToElement(new
        {
        })),
            new DockerProfile("weaveport-poc-" + language + ":1"), new Callbacks(), ["documents.read"]);
        ContractChecks.Successful(await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new
        {
        })));
        await using var resources = new ResourceSampler(session.Instance, TimeProvider.System);
        await resources.WaitForFirstSampleAsync();
        foreach ((string operation, int size) in new[] { ("echo", 16), ("echo", 65536), ("search", 0) })
        {
            await MeasureOperationAsync(session, language, operation, size);
        }
        await resources.StopAsync();
        await File.WriteAllTextAsync(Path.Combine(output, language + "-resource-samples.json"), JsonSerializer.Serialize(resources.Samples, new JsonSerializerOptions { WriteIndented = true }));
        _lines.Add($"- {language}: {resources.Samples.Count} Docker resource samples retained.");
    }
    private async Task MeasureOperationAsync(IPluginSession session, string language, string operation, int size)
    {
        JsonElement payload = operation == "search" ? JsonSerializer.SerializeToElement(new
        {
            query = "weave"
        }) : JsonSerializer.SerializeToElement(new
        {
            text = new string('x', size)
        });
        for (int n = 0; n < WarmupRequests; n++)
        {
            ContractChecks.Successful(await session.InvokeAsync(operation, payload));
        }

        var requests = new List<InvocationResult>();
        for (int n = 0; n < MeasuredRequests; n++)
        {
            requests.Add(await session.InvokeAsync(operation, payload));
        }

        double[] times = requests.Select(r => r.ElapsedMs).ToArray();
        int errors = requests.Count(r => r.Status != "ok");
        double p99 = ContractChecks.Percentile(times, 99);
        bool passed = errors == 0 && (operation != "search" || p99 <= SearchP99LimitMs);
        if (!passed)
        {
            _failures++;
        }

        _measurements.Add(new
        {
            language,
            operation,
            payloadCharacters = size,
            warmup = WarmupRequests,
            requests = requests.Select(r => new { r.Status, r.ElapsedMs, r.Instance, r.MayHaveExecuted }).ToArray(),
            p50 = ContractChecks.Percentile(times, 50),
            p95 = ContractChecks.Percentile(times, 95),
            p99,
            errors,
            passed
        });
        string line = $"{language} {operation} {size}: {requests.Count} requests, p99={p99:F3} ms, errors={errors}, passed={passed}";
        Console.WriteLine(line);
        _lines.Add("- " + line);
    }
}
