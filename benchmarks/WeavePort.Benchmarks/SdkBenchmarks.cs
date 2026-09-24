using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using System.Text.Json;
using WeavePort.SdkFixture;

namespace WeavePort.Benchmarks;

[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 6)]
[IterationTime(250)]
public class SdkBenchmarks
{
    private Harness _harness = null!;
    [Params("csharp", "python", "typescript")] public string Language { get; set; } = "csharp";
    [Params("local", "gateway")] public string Topology { get; set; } = "local";
    [Params("tiny", "callback", "list-64k", "list-8m")] public string Case { get; set; } = "tiny";
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _harness = await Harness.CreateAsync([Language], Topology == "gateway");
        for (int i = 0; i < 8; i++)
        {
            Workload.Check(await Workload.ExecuteAsync(_harness.Clients[0], Case), Case, "tenant-0");
        }
    }
    [Benchmark]
    public async Task<JsonElement[]> CompleteSdkResult()
    {
        JsonElement[] result = await Workload.ExecuteAsync(_harness.Clients[0], Case);
        Workload.Check(result, Case, "tenant-0");
        return result;
    }
    [GlobalCleanup] public async Task CleanupAsync() => await _harness.DisposeAsync();
}
