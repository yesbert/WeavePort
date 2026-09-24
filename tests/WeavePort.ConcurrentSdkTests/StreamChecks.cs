using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class StreamChecks
{
    internal static async Task RunAsync(string[] args)
    {
        string root = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Environment.CurrentDirectory);
        var languages = new[] { "csharp", "python", "typescript" };
        await Task.WhenAll(languages.Select(language => CheckAsync(Profile(root, language), language)));
        Console.WriteLine("PASS: all-language stream deadlines, live delivery, pressure residency, paused cancellation, late disposal and queue cancellation.");
    }

    internal static ProcessProfile Profile(string root, string language)
    {
        string executable = language == "csharp" ? Environment.ProcessPath! : FindExecutable(language == "python" ? "python3" : "node");
        string[] arguments = language == "csharp" ? [typeof(StreamChecks).Assembly.Location, "--stream-worker"] : [Path.Combine(root, "tests/WeavePort.ConcurrentSdkTests/fixtures", language == "python" ? "streams.py" : "streams.mjs")];
        string? sdk = language == "csharp" ? null : Environment.GetEnvironmentVariable(language == "python" ? "WP_SHARED_PYTHON_SDK" : "WP_SHARED_NODE_SDK");
        if (sdk is not null)
        {
            arguments = [.. arguments, "--sdk-path", sdk];
        }

        return new ProcessProfile(executable, arguments, trustedCode: true, reservedMemoryMiB: 64, timeout: TimeSpan.FromSeconds(5)) { Reconstructible = true };
    }

    private static string FindExecutable(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory, name)).First(File.Exists);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static PluginHost Host(int workers = 2) => new(new SchedulingOptions { MaximumWorkers = workers, MemoryBudgetMiB = workers * 64, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0, QueueTimeout = TimeSpan.FromSeconds(5), NormalTimeout = TimeSpan.FromSeconds(5) });
    private static async Task<LocalPluginClient> ClientAsync(PluginHost host, ProcessProfile profile, string tenant) => new(await host.BindAsync(new(tenant, "streams", "1", "test", Json(new { })), profile, new NoCallbacks(), []));

    private static async Task CheckAsync(ProcessProfile profile, string language)
    {
        await DeadlinesAsync(profile, language);
        await PressureAsync(profile, language);
        await CancelPausedAsync(profile, language);
        await QueueCancellationAsync(profile, language);
        Console.WriteLine($"PASS stream qualification: {language}");
    }

    private static async Task DeadlinesAsync(ProcessProfile profile, string language)
    {
        await using var host = Host();
        await using var client = await ClientAsync(host, profile, "deadline-" + language);
        await client.CallAsync("echo", Json(new
        {
        }));
        long started = Stopwatch.GetTimestamp();
        await using (var iterator = client.StreamAsync("slow", Json(new
        {
            firstDelay = 12000,
            nextDelay = 0
        })).GetAsyncEnumerator())
        {
            Check(await iterator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(18)), "12-second first stream item");
            Check(Stopwatch.GetElapsedTime(started).TotalSeconds >= 11, "Producer delay was preserved");
        }
        started = Stopwatch.GetTimestamp();
        try
        {
            await client.CallAsync("echo", Json(new
            {
                delay = 12000
            }));
            throw new InvalidOperationException("Unary deadline was bypassed.");
        }
        catch (PluginCallException error) when (error.Status == "timeout") { }
        Check(Stopwatch.GetElapsedTime(started).TotalSeconds < 8, "Unary retains its five-second timeout");
        await client.CallAsync("echo", Json(new
        {
        }));
        started = Stopwatch.GetTimestamp();
        await using var live = client.StreamAsync("slow", Json(new
        {
            nextDelay = 500
        })).GetAsyncEnumerator();
        Check(await live.MoveNextAsync(), "First live item");
        Check(Stopwatch.GetElapsedTime(started).TotalMilliseconds < 400, "Available first item was not held for batching");
    }

    private static async Task PressureAsync(ProcessProfile profile, string language)
    {
        await using var host = Host();
        await using var stream = await ClientAsync(host, profile, "stream-" + language);
        await using var idle = await ClientAsync(host, profile, "idle-" + language);
        await using var demand = await ClientAsync(host, profile, "demand-" + language);
        await idle.CallAsync("echo", Json(new
        {
        }));
        await using var iterator = stream.StreamAsync("slow", Json(new
        {
            nextDelay = 500
        })).GetAsyncEnumerator();
        Check(await iterator.MoveNextAsync(), "Stream begins");
        int pid = iterator.Current.GetProperty("pid").GetInt32();
        await demand.CallAsync("echo", Json(new
        {
        }));
        Check(host.Scheduling!.Evictions >= 1, "Idle neighbor evicted under pressure");
        Check(await iterator.MoveNextAsync(), "Paused stream resumes");
        Check(iterator.Current.GetProperty("pid").GetInt32() == pid, "Paused stream worker remained resident");
    }

    private static async Task CancelPausedAsync(ProcessProfile profile, string language)
    {
        await using var host = Host(1);
        await using var client = await ClientAsync(host, profile, "cancel-" + language);
        using var stop = new CancellationTokenSource();
        var iterator = client.StreamAsync("slow", Json(new
        {
            nextDelay = 10000
        }), stop.Token).GetAsyncEnumerator();
        Check(await iterator.MoveNextAsync(), "Paused stream begins");
        stop.Cancel();
        await WaitAsync(() => host.Scheduling!.Active == 0, "Paused cancellation releases admission");
        int pid = (await client.CallAsync("echo", Json(new
        {
        }))).GetProperty("pid").GetInt32();
        await iterator.DisposeAsync();
        int after = (await client.CallAsync("echo", Json(new
        {
        }))).GetProperty("pid").GetInt32();
        Check(after == pid, "Late old iterator disposal cannot restart the newer worker");
    }

    private static async Task QueueCancellationAsync(ProcessProfile profile, string language)
    {
        await using var host = Host(1);
        await using var first = await ClientAsync(host, profile, "first-" + language);
        await using var second = await ClientAsync(host, profile, "second-" + language);
        await using var iterator = first.StreamAsync("slow", Json(new
        {
            nextDelay = 300
        })).GetAsyncEnumerator();
        Check(await iterator.MoveNextAsync(), "First stream reserves sole worker");
        using var stop = new CancellationTokenSource(50);
        try
        {
            await using var waiting = second.StreamAsync("slow", Json(new
            {
            }), stop.Token).GetAsyncEnumerator();
            await waiting.MoveNextAsync();
            throw new InvalidOperationException("Queued stream cancellation not observed.");
        }
        catch (Exception error) when (error is IOException or OperationCanceledException) { }
        await iterator.DisposeAsync();
        await second.CallAsync("echo", Json(new
        {
        }));
        Check(host.Scheduling!.Failure is null, "Queue cancellation must not stop scheduler");
    }

    private static async Task WaitAsync(Func<bool> ready, string message)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready())
        {
            await Task.Delay(10, stop.Token);
        }

        Check(ready(), message);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
