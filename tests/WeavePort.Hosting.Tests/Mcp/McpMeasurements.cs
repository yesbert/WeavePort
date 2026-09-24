using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class McpMeasurements
{
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };
    internal static async Task RunAsync(string executable, string fixtureRoot, string output)
    {
        string workspace = Path.Combine(Path.GetTempPath(), "wp-mcp-measure-" + Guid.NewGuid().ToString("N"));
        var runs = new List<object>();
        try
        {
            for (int repeat = 0; repeat < 5; repeat++)
            {
                runs.AddRange(await MeasureRepeatAsync(executable, fixtureRoot, workspace, repeat));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                scope = "Sequential C# PluginHost calls to native TypeScript SDK and official MCP SDK2; same echo operation, different SDKs and envelopes. Five alternating repetitions, not maximum throughput or pure transport cost.",
                runtime = Environment.Version.ToString(),
                hostingSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
                nodeSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))),
                passed = true,
                runs
            }, ReportOptions));
            Console.WriteLine("PASS 18000 measured complete calls across native and two MCP revisions");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, true);
            }
        }
    }
    private static async Task<List<object>> MeasureRepeatAsync(string executable, string fixtureRoot, string workspace, int repeat)
    {
        var protocols = new[] { ProcessProtocol.Native, ProcessProtocol.Mcp20251125, ProcessProtocol.Mcp20260728 };
        if (repeat % 2 != 0)
        {
            Array.Reverse(protocols);
        }

        var runs = new List<object>();
        foreach (var protocol in protocols)
        {
            runs.AddRange(await MeasureProtocolAsync(executable, fixtureRoot, workspace, repeat, protocol));
        }
        return runs;
    }

    private static async Task<List<object>> MeasureProtocolAsync(string executable, string fixtureRoot, string workspace, int repeat, ProcessProtocol protocol)
    {
        await using var host = new PluginHost();
        var profile = new ProcessProfile(executable, [Path.Combine(fixtureRoot, protocol == ProcessProtocol.Native ? "native-server.mjs" : "server.mjs")], true, workspace) { Protocol = protocol };
        await using var session = await host.BindAsync(new PluginContext("measure", "echo", "1", "measure", JsonSerializer.SerializeToElement(new
        {
        })), profile, new Callbacks(), []);
        long start = Stopwatch.GetTimestamp();
        await EchoAsync(session, protocol, "start");
        double firstCallMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var runs = new List<object>();
        foreach (int width in new[] { 16, 65536 })
        {
            runs.Add(await MeasureWidthAsync(session, protocol, repeat, width, firstCallMs));
        }
        return runs;
    }

    private static async Task<object> MeasureWidthAsync(IPluginSession session, ProcessProtocol protocol, int repeat, int width, double firstCallMs)
    {
        string text = new('x', width);
        for (int i = 0; i < 30; i++)
        {
            await EchoAsync(session, protocol, text);
        }

        int count = width == 16 ? 1000 : 200;
        var samples = new double[count];
        long allocated = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < count; i++)
        {
            long before = Stopwatch.GetTimestamp();
            await EchoAsync(session, protocol, text);
            samples[i] = Stopwatch.GetElapsedTime(before).TotalMilliseconds;
        }
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        Array.Sort(samples);
        return new
        {
            protocol = protocol.ToString(),
            repeat,
            width,
            count,
            firstCallMs,
            meanMs = samples.Average(),
            p50Ms = samples[count / 2],
            p99Ms = samples[(int)(count * .99)],
            callerAllocatedBytesPerCall = (double)allocated / count,
            samples
        };
    }

    private static async Task EchoAsync(IPluginSession session, ProcessProtocol protocol, string text)
    {
        var arguments = new
        {
            text
        };
        var payload = protocol == ProcessProtocol.Native
            ? JsonSerializer.SerializeToElement(new
            {
                operation = "echo",
                input = arguments
            })
            : JsonSerializer.SerializeToElement(new
            {
                name = "echo",
                arguments
            });
        var result = await session.InvokeAsync(protocol == ProcessProtocol.Native ? "$sdk.call" : "tools/call", payload);
        if (result.Status != "ok")
        {
            throw new Exception("Measurement call failed: " + result.Status);
        }

        var value = protocol == ProcessProtocol.Native ? result.Value : result.Value.GetProperty("structuredContent");
        if (value.GetProperty("text").GetString() != text)
        {
            throw new Exception("Incorrect complete result");
        }
    }
    private sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new Exception("Unexpected callback");
    }
}
