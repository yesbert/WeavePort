using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;
using WeavePort.LoadTests;

string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var measurements = new List<object>();
var lines = new List<string> { "# Per-request latency and sampled container resources", "", "These are request-level observations, separate from BenchmarkDotNet iteration statistics. Resource samples are periodic observations, not an exact peak-memory measurement.", "" };
int failures = 0;
await using var host = new PluginHost();
foreach (string language in new[] { "csharp", "python", "typescript" })
{
    await using IPluginSession session = await host.BindAsync(new PluginContext("load-" + language, "demo", "1", "default", JsonSerializer.SerializeToElement(new { })),
        new DockerProfile("weaveport-poc-" + language + ":1"), new Callbacks(), ["documents.read"]);
    ContractChecks.Successful(await session.InvokeAsync("echo", JsonSerializer.SerializeToElement(new { })));
    await using var resources = new ResourceSampler(session.Instance, TimeProvider.System);
    await resources.WaitForFirstSampleAsync();
    foreach ((string operation, int size) in new[] { ("echo", 16), ("echo", 65536), ("search", 0) })
    {
        JsonElement payload = operation == "search" ? JsonSerializer.SerializeToElement(new { query = "weave" }) : JsonSerializer.SerializeToElement(new { text = new string('x', size) });
        for (int n = 0; n < 20; n++) ContractChecks.Successful(await session.InvokeAsync(operation, payload));
        var requests = new List<InvocationResult>();
        for (int n = 0; n < 300; n++) requests.Add(await session.InvokeAsync(operation, payload));
        double[] times = requests.Select(r => r.ElapsedMs).ToArray();
        int errors = requests.Count(r => r.Status != "ok");
        double p99 = ContractChecks.Percentile(times, 99);
        bool passed = errors == 0 && (operation != "search" || p99 <= 100);
        if (!passed) failures++;
        measurements.Add(new { language, operation, payloadCharacters = size, warmup = 20,
            requests = requests.Select(r => new { r.Status, r.ElapsedMs, r.Instance, r.MayHaveExecuted }).ToArray(),
            p50 = ContractChecks.Percentile(times, 50), p95 = ContractChecks.Percentile(times, 95), p99, errors, passed });
        string line = $"{language} {operation} {size}: {requests.Count} requests, p99={p99:F3} ms, errors={errors}, passed={passed}";
        Console.WriteLine(line);
        lines.Add("- " + line);
    }
    await resources.StopAsync();
    await File.WriteAllTextAsync(Path.Combine(output, language + "-resource-samples.json"), JsonSerializer.Serialize(resources.Samples, new JsonSerializerOptions { WriteIndented = true }));
    lines.Add($"- {language}: {resources.Samples.Count} Docker resource samples retained.");
}
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, measurements, scope = "sequential per-request latency; one local machine; failed calls remain in raw samples" }, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(output, "report.md"), string.Join(Environment.NewLine, lines));
Environment.ExitCode = failures == 0 ? 0 : 1;

internal sealed class Callbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new[] { new { id = call.Context.Tenant + "-1", text = "WeavePort document" } }));
    }
}
