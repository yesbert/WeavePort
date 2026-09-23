using System.Text;
using System.Diagnostics;
using System.Text.Json;
using WeavePort.Hosting;

if (args.Contains("--lifecycle-worker"))
{
    await LifecycleChecks.WorkerAsync();
    return;
}
if (args.Contains("--mcp-worker"))
{
    await McpFixture.RunAsync(args.Last());
    return;
}
if (args.Contains("--mcp-benchmark"))
{
    await McpMeasurements.RunAsync(args[^3], args[^2], args[^1]);
    return;
}
if (args.Contains("--mcp-interop"))
{
    await McpChecks.InteropAsync(args[^2], args[^1]);
    return;
}
if (args.Contains("--probe-measure"))
{
    var measurements = new List<object>();
    for (int i = 1; i < args.Length; i += 2)
    {
        var samples = new List<double>();
        for (int sample = 0; sample < 25; sample++)
        {
            var watch = Stopwatch.StartNew();
            await RuntimeProbe.RunAsync(args[i + 1], args[i], default);
            if (sample >= 5) samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        measurements.Add(new { ecosystem = args[i], count = samples.Count, medianMs = samples[samples.Count / 2], minMs = samples[0], maxMs = samples[^1] });
    }
    Console.WriteLine(JsonSerializer.Serialize(measurements));
    return;
}
if (args.Contains("--framework-oracle"))
{
    using var cases = JsonDocument.Parse(File.ReadAllText(args[^1]));
    foreach (var item in cases.RootElement.EnumerateArray())
    {
        string requirement = DotnetRequirements.Read(item.GetProperty("config").GetString()!);
        bool actual = DotnetFrameworks.Matches(requirement, item.GetProperty("inventory").GetString()!);
        if (actual != item.GetProperty("expected").GetBoolean()) throw new Exception("Framework oracle mismatch " + requirement);
    }
    Console.WriteLine($"PASS {cases.RootElement.GetArrayLength()} real dotnet framework resolution comparisons");
    return;
}
if (args.Contains("--portable"))
{
    await PortableInstallationChecks.RunAsync();
    return;
}
await ConsumerFindingChecks.RunAsync();
await PortableInstallationChecks.RunAsync();
Console.WriteLine($"PASS {await McpChecks.RunAsync()} MCP protocol, authority and lifecycle assertions");
ManifestChecks.Run();
await DisposalChecks.RunAsync();
await ShutdownRaceChecks.RunAsync();
await LockOrderChecks.RunAsync();
await DockerCommandChecks.RunAsync();
Console.WriteLine($"PASS {await QuarantineChecks.RunAsync()} quarantine age and reservation assertions");
Console.WriteLine($"PASS {await AdmissionChecks.RunAsync()} concurrent-start admission assertions");
JsonSizeChecks.Run();
await FragmentChecks.RunAsync();
int checks = 0;
foreach (int chunk in new[] { 1, 7, 4096, 1048576 })
{
    string first = "{\"text\":\"Grüße 🐝\"}";
    string large = "\"" + new string('x', 65536) + "\"";
    using var stream = new ChunkStream(Encoding.UTF8.GetBytes(first + "\n" + large + "\n42\n"), chunk);
    using var frames = new Frames(stream);
    JsonElement owned = await frames.ReadAsync(default);
    Assert((await frames.ReadAsync(default)).GetString()!.Length == 65536, "large split frame");
    Assert((await frames.ReadAsync(default)).GetInt32() == 42, "carryover");
    frames.Dispose();
    Assert(owned.GetProperty("text").GetString() == "Grüße 🐝", "owned lifetime");
}
foreach ((byte[] data, Type expected) in new[]
{
    (Encoding.UTF8.GetBytes("{\n"), typeof(JsonException)),
    (Encoding.UTF8.GetBytes("42"), typeof(EndOfStreamException)),
    (Encoding.UTF8.GetBytes(new string(' ', Frames.MaximumBytes + 1) + "\n"), typeof(InvalidDataException)),
    (Encoding.UTF8.GetBytes(new string('[', 33) + "0" + new string(']', 33) + "\n"), typeof(JsonException)),
    (new byte[] { 34, 255, 34, 10 }, typeof(InvalidDataException))
})
{
    using var stream = new MemoryStream(data);
    using var frames = new Frames(stream);
    try { await frames.ReadAsync(default); throw new Exception("Expected rejection: " + expected); }
    catch (Exception error) when (expected.IsInstanceOfType(error)) { checks++; }
}
using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"" + new string('x', Frames.MaximumBytes - 2) + "\"\n")))
using (var frames = new Frames(stream))
    Assert((await frames.ReadAsync(default)).GetString()!.Length == Frames.MaximumBytes - 2, "exact byte ceiling");
using (var input = new CancelStream())
using (var frames = new Frames(input))
using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(20)))
{
    try { await frames.ReadAsync(cancel.Token); throw new Exception("Cancellation missing"); }
    catch (OperationCanceledException) { checks++; }
}
using (var output = new MemoryStream())
{
    var value = new CallbackResultFrame("callback-result", "id", "cid", JsonSerializer.SerializeToElement(new string('x', Frames.MaximumBytes)));
    try { await Frames.WriteAsync(output, value, WireJson.Default.CallbackResultFrame, default); throw new Exception("Oversize output accepted"); }
    catch (InvalidDataException) { Assert(output.Length == 0, "oversize never partially dispatched"); }
}
var activities = new List<Activity>();
using (var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == "WeavePort.Transport",
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStopped = activities.Add
})
{
    ActivitySource.AddActivityListener(listener);
    using var input = new ChunkStream(Encoding.UTF8.GetBytes("42\n"), 1);
    using var reader = new Frames(input);
    Assert((await reader.ReadAsync(default)).GetInt32() == 42, "diagnostics preserve result");
    using var output = new MemoryStream();
    await Frames.WriteAsync(output, new CallbackResultFrame("callback-result", "id", "cid", JsonSerializer.SerializeToElement(42)), WireJson.Default.CallbackResultFrame, default);
    Assert(activities.Count == 2 && (int)activities[0].GetTagItem("reads")! == 3, "diagnostic read count");
    Assert((int)activities[0].GetTagItem("bytes")! == 3 && (int)activities[1].GetTagItem("bytes")! == output.Length, "diagnostic byte count");
    Assert(activities.All(a => a.TagObjects.All(t => t.Value is double or int)) && activities.All(a => (double)a.GetTagItem("complete.ms")! >= 0), "diagnostics contain only numeric measurements");
}
Console.WriteLine($"PASS {checks} transport assertions: split/multiple/UTF8/ownership/limits/depth/EOF/cancellation/atomic output");
Console.WriteLine($"PASS {await SocketChecks.RunAsync()} socket platform/path/connection assertions");
Console.WriteLine($"PASS {EnvelopeChecks.Run()} hostile envelope assertions");
Console.WriteLine($"PASS {await ProcessSocketChecks.RunAsync()} native endpoint ownership assertions");
Console.WriteLine($"PASS {await LifecycleChecks.RunAsync()} lifecycle and safe diagnostics assertions");
void Assert(bool value, string name) { if (!value) throw new Exception(name); checks++; }
sealed class ChunkStream(byte[] bytes, int chunk) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], token);
}
sealed class CancelStream : MemoryStream
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(Timeout.Infinite, token); return 0; }
}
