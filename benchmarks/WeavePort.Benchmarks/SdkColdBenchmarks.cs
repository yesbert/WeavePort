using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using System.Text.Json;
using WeavePort.SdkFixture;

namespace WeavePort.Benchmarks;

[JsonExporterAttribute.Full]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 6, invocationCount: 1)]
public class SdkColdBenchmarks
{
    [Params("csharp", "python", "typescript")] public string Language { get; set; } = "csharp";
    [Params("local", "gateway")] public string Topology { get; set; } = "local";
    [Benchmark]
    public async Task<JsonElement[]> StartCallAndDispose()
    {
        await using var harness = await Harness.CreateAsync([Language], Topology == "gateway");
        var result = await Workload.ExecuteAsync(harness.Clients[0], "callback");
        Workload.Check(result, "callback", "tenant-0");
        return result;
    }
}
