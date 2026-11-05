using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static partial class FileExchangeChecks
{
    private static async Task OutputLimitsAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var bytes = new RecordingDestination();
        InvocationResult size = await session.InvokeAsync("script", Script(Create("a", "text/plain"), Write("a", new byte[4096]), Write("a", new byte[4096])),
            new PluginFiles([], bytes, new PluginFileLimits(2, 5000)));
        Expect(size.Status == "output-limit" && bytes.Events.Contains("abort:a:Limit") && !bytes.Events.Contains("commit:a") && bytes.Data["a"].Length == 4096, "total output size is enforced before the writer");
        var count = new RecordingDestination();
        InvocationResult outputs = await session.InvokeAsync("script", Script(Create("a", "text/plain"), Create("b", "text/plain")),
            new PluginFiles([], count, new PluginFileLimits(1, 5000)));
        Expect(outputs.Status == "output-limit" && count.Events.SequenceEqual(["create:a:text/plain", "abort:a:Limit", "dispose:a"]), "output count is enforced");
    }

    private static async Task FailuresAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var open = new RecordingDestination();
        InvocationResult incomplete = await session.InvokeAsync("script", Script(Create("a", "text/plain"), Write("a", [1])),
            new PluginFiles([], open, new PluginFileLimits(1, 100)));
        Expect(incomplete.Status == "output-incomplete" && incomplete.MayHaveExecuted && open.Events.Contains("abort:a:Failed"), "an output left open fails the call");
        var failed = new RecordingDestination();
        InvocationResult error = await session.InvokeAsync("script", Script(Create("a", "text/plain"), Complete("a"), new
        {
            op = "fail"
        }),
            new PluginFiles([], failed, new PluginFileLimits(1, 100)));
        Expect(error.Status == "failed" && error.Failure?.Code == "sdk-error" && failed.Events.Contains("abort:a:Failed") && !failed.Events.Contains("commit:a"), "a completed output is aborted when the plugin fails");
    }

    private static async Task CommitFailureAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var destination = new RecordingDestination(failCommitFor: "b");
        InvocationResult result = await session.InvokeAsync("script", Script(Create("a", "text/plain"), Complete("a"), Create("b", "text/plain"), Complete("b")),
            new PluginFiles([], destination, new PluginFileLimits(2, 100)));
        Expect(result.Status == "output-commit-failed" && result.MayHaveExecuted && result.Outputs.Single().Name == "a", "a partial commit names what was committed");
        Expect(destination.Events.Contains("abort:b:CommitFailed") && !destination.Events.Any(item => item.StartsWith("abort:a", StringComparison.Ordinal)), "only uncommitted outputs are aborted");
    }

    private static async Task WriterReleaseAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var oversized = new RecordingDestination();
        InvocationResult big = await session.InvokeAsync("script", JsonSerializer.SerializeToElement(new
        {
            steps = new[] { Create("a", "text/plain"), Complete("a") },
            pad = 600_000
        }), new PluginFiles([], oversized, new PluginFileLimits(1, 100)));
        Expect(big.Status == "value-limit" && !oversized.Events.Contains("commit:a") && oversized.Events.Contains("abort:a:Failed"), "a result over the unary limit aborts outputs before any commit");
        var failing = new RecordingDestination(disposal: "throw");
        InvocationResult disposed = await session.InvokeAsync("script", Script(Create("a", "text/plain"), Complete("a")), new PluginFiles([], failing, new PluginFileLimits(1, 100)));
        Expect(disposed.Status == "cleanup-error" && disposed.Failure?.CleanupFailed == true && disposed.Outputs.Single().Name == "a", "a writer release failure after commit is reported with the committed outputs");
        var stalled = new RecordingDestination(disposal: "hang");
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        InvocationResult hung = await session.InvokeAsync("script", Script(Create("a", "text/plain"), new
        {
            op = "fail"
        }), new PluginFiles([], stalled, new PluginFileLimits(1, 100)));
        Expect(hung.Status == "failed" && hung.Failure?.CleanupFailed == true && System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20), "a stalled writer release is bounded by the cleanup grace period");
    }

    private static async Task CancellationAsync(PluginHost host, string root)
    {
        await using var session = await BindAsync(host, root);
        var destination = new RecordingDestination();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        InvocationResult result = await session.InvokeAsync("script", Script(Create("a", "text/plain"), new
        {
            op = "hang"
        }),
            new PluginFiles([], destination, new PluginFileLimits(1, 100)), cancel.Token);
        Expect(result.Status == "cancelled" && destination.Events.Contains("abort:a:Cancelled"), "cancellation aborts outputs");
    }

    private static async Task ScheduledAsync(string root)
    {
        await using var host = new PluginHost(new SchedulingOptions
        {
            MaximumWorkers = 1,
            MemoryBudgetMiB = 64,
            MaximumPristineWorkers = 0,
            MaximumHeavyCalls = 0
        });
        await using var session = await BindAsync(host, root);
        var destination = new RecordingDestination();
        InvocationResult result = await session.InvokeAsync("script", Script(Read("sheet", 0, 4096), Create("a", "text/plain"), Write("a", [7]), Complete("a")),
            new PluginFiles([Input("sheet", Source)], destination, new PluginFileLimits(1, 100)));
        InvocationResult stream = await session.InvokeAsync("$sdk.start", Script(), new PluginFiles([Input("sheet", Source)]));
        Expect(stream.Status == "files-unsupported" && !stream.MayHaveExecuted, "scheduled bindings refuse files for stream operations like direct bindings");
        Expect(session is ScheduledPlugin && result.Status == "ok" && result.Outputs.Single().Length == 1 && Sha(result.Value.GetProperty("replies")[0]) == Hash(Source[..4096]), "scheduled bindings carry files through the queue");
    }

    private static async Task ClientAndBuiltInsAsync(PluginHost host, string root)
    {
        string inputPath = Path.Combine(root, "input.bin");
        string outputs = Directory.CreateDirectory(Path.Combine(root, "outputs")).FullName;
        await File.WriteAllBytesAsync(inputPath, Source);
        using var handle = File.OpenHandle(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        var input = new PluginInput("sheet", Source.Length, "application/octet-stream", PluginInputReader.FromFile(handle));
        var files = new PluginFiles([input], PluginOutputDestination.ToDirectory(outputs), new PluginFileLimits(2, 1 << 20));
        await using var client = new LocalPluginClient(await BindAsync(host, root));
        var result = await client.CallWithFilesAsync("script", Script(Read("sheet", 100_000, 65536), Create("copy.bin", "application/octet-stream"), Write("copy.bin", Source[100_000..165_536]), Complete("copy.bin")), files);
        Expect(Sha(result.Value.GetProperty("replies")[0]) == Hash(Source[100_000..165_536]) && result.Outputs.Single().Length == 65536, "local client reads a file handle and reports outputs");
        Expect((await File.ReadAllBytesAsync(Path.Combine(outputs, "copy.bin"))).AsSpan().SequenceEqual(Source.AsSpan(100_000, 65536)), "directory destination commits the exact bytes");
        try
        {
            await client.CallWithFilesAsync("script", Script(Create("lost.bin", "text/plain"), Write("lost.bin", [1, 2, 3]), Complete("lost.bin"), new
            {
                op = "fail"
            }), files);
            throw new Exception("Failing file call succeeded.");
        }
        catch (PluginCallException error) when (error.MayHaveExecuted && error.Outputs.Count == 0)
        {
            _checks++;
        }

        Expect(Directory.GetFiles(outputs).Select(Path.GetFileName).SequenceEqual(["copy.bin"]), "an aborted directory output leaves no file behind");
    }
}
