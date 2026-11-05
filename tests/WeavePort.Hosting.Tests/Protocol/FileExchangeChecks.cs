using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

// End-to-end host checks against a real worker process speaking the file operations.
internal static partial class FileExchangeChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static readonly byte[] Source = RandomNumberGenerator.GetBytes(300_000);
    private static int _checks;
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "wp-files-workspace-" + Guid.NewGuid().ToString("N"));

    internal static async Task<int> RunAsync()
    {
        _checks = 0;
        string root = Path.Combine(Path.GetTempPath(), "wp-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var host = new PluginHost();
            await UnsupportedAsync(host, root);
            await RoundTripAsync(host, root);
            await RefusalsAsync(host, root);
            await ReadBudgetAsync(host, root);
            await OutputLimitsAsync(host, root);
            await FailuresAsync(host, root);
            await CommitFailureAsync(host, root);
            await CancellationAsync(host, root);
            await ClientAndBuiltInsAsync(host, root);
            await ScheduledAsync(root);
            return _checks;
        }
        finally
        {
            Directory.Delete(root, true);
            if (Directory.Exists(Workspace))
            {
                Directory.Delete(Workspace, true);
            }
        }
    }

    private static async Task UnsupportedAsync(PluginHost host, string root)
    {
        await using var legacy = await BindAsync(host, root, advertise: false);
        var files = new PluginFiles([Input("sheet", Source)]);
        InvocationResult refused = await legacy.InvokeAsync("script", Script(), files);
        Expect(refused.Status == "files-unsupported" && !refused.MayHaveExecuted, "worker without capability refuses before dispatch");
        Expect((await legacy.InvokeAsync("script", Script())).Status == "ok", "calls without files still work on that worker");
        await legacy.RestartAsync();
        int workers = host.Snapshot.Workers;
        InvocationResult known = await legacy.InvokeAsync("script", Script(), files);
        Expect(known.Status == "files-unsupported" && host.Snapshot.Workers == workers, "a binding whose worker declined files refuses without starting another worker");
        await using var session = await BindAsync(host, root);
        InvocationResult stream = await session.InvokeAsync("$sdk.start", Script(), files);
        Expect(stream.Status == "files-unsupported" && !stream.MayHaveExecuted, "stream operations refuse files before dispatch");
    }

    private static async Task RoundTripAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var destination = new RecordingDestination();
        byte[] output = RandomNumberGenerator.GetBytes(70_000);
        var steps = new List<object>
        {
            Read("sheet", Source.Length - 4096, 4096),
            Read("sheet", 0, 65536),
            Read("sheet", Source.Length, 4096),
            Create("out.bin", "application/octet-stream"),
            Write("out.bin", output[..65536]),
            Write("out.bin", output[65536..]),
            Complete("out.bin")
        };
        steps.AddRange(Enumerable.Range(0, 12).Select(_ => Read("sheet", 0, 4096)));
        var files = new PluginFiles([Input("sheet", Source)], destination, new PluginFileLimits(2, 1 << 20));
        InvocationResult result = await session.InvokeAsync("script", Script([.. steps]), files);
        Expect(result.Status == "ok", "file round trip succeeds beyond the callback budget");
        JsonElement replies = result.Value.GetProperty("replies");
        Expect(Sha(replies[0]) == Hash(Source[^4096..]) && Sha(replies[1]) == Hash(Source[..65536]), "reads at the end and the start return exact bytes");
        Expect(replies[2].GetProperty("length").GetInt32() == 0, "a read at the end returns no bytes");
        Expect(replies[6].GetProperty("length").GetInt64() == output.Length, "complete reports the output length");
        JsonElement declared = result.Value.GetProperty("files");
        Expect(declared.GetProperty("inputs")[0].GetProperty("name").GetString() == "sheet" && declared.GetProperty("inputs")[0].GetProperty("length").GetInt64() == Source.Length && declared.GetProperty("outputs").GetProperty("maximumCount").GetInt32() == 2, "invoke frame carries host-owned descriptors");
        Expect(result.Outputs.Count == 1 && result.Outputs[0] == new PluginOutputInfo("out.bin", "application/octet-stream", output.Length), "committed output is reported");
        Expect(destination.Data["out.bin"].ToArray().AsSpan().SequenceEqual(output), "writer received the exact output bytes");
        Expect(destination.Events.SequenceEqual(["create:out.bin:application/octet-stream", "commit:out.bin", "dispose:out.bin"]), "successful output is committed once and disposed");
    }

    private static async Task RefusalsAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        InvocationResult inputsOnly = await session.InvokeAsync("script", Script(
            Read("missing", 0, 4096), Read("sheet", 0, 100), Read("sheet", -1, 4096), Create("out", "text/plain")),
            new PluginFiles([Input("sheet", Source)]));
        Expect(Errors(inputsOnly) == "input-not-found,invalid-range,invalid-range,output-not-permitted" && inputsOnly.Status == "ok", "recoverable refusals reach the plugin without failing the call");
        var destination = new RecordingDestination();
        InvocationResult outputs = await session.InvokeAsync("script", Script(
            Create("a", "text/plain"), Create("a", "text/plain"), Complete("a"), Write("a", [1]), Write("other", [1])),
            new PluginFiles([], destination, new PluginFileLimits(4, 100)));
        Expect(Errors(outputs) == ",output-exists,,output-closed,output-not-permitted" && outputs.Status == "ok" && outputs.Outputs.Single().Length == 0, "output misuse is refused and the completed output commits");
    }

    private static async Task ReadBudgetAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var files = new PluginFiles([Input("sheet", Source[..10_000])], null, new PluginFileLimits(0, 0, 8192));
        InvocationResult result = await session.InvokeAsync("script", Script(Read("sheet", 0, 4096), Read("sheet", 4096, 4096), Read("sheet", 8192, 4096)), files);
        Expect(result.Status == "input-read-limit" && result.MayHaveExecuted && result.Failure?.Code == "input-read-limit", "reading beyond the budget fails the call");
        Expect((await session.InvokeAsync("script", Script())).Status == "ok", "the binding recovers after a budget failure");
        var atEnd = new PluginFiles([Input("sheet", Source[..10_000])], null, new PluginFileLimits(0, 0, 8192));
        InvocationResult looping = await session.InvokeAsync("script", Script(Read("sheet", 10_000, 4096), Read("sheet", 10_000, 4096), Read("sheet", 10_000, 4096)), atEnd);
        Expect(looping.Status == "input-read-limit", "repeated reads at the end of an input are charged against the budget");
    }
}
