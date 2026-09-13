using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class LifecycleChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private const string Secret = "sentinel-private-payload-and-error";

    internal static async Task WorkerAsync()
    {
        Console.WriteLine("{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}");
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var request = JsonDocument.Parse(line);
            string id = request.RootElement.GetProperty("id").GetString()!;
            string operation = request.RootElement.GetProperty("operation").GetString()!;
            if (operation == "callback")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { type = "callback", id, callbackId = "1", operation = "probe", payload = Empty }));
                await Task.Delay(Timeout.Infinite);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { type = "result", id, value = 42 }));
        }
    }

    internal static async Task<int> RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "wp-lifecycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            int checks = await SetupAsync(root);
            checks += await CancellationAsync(root);
            checks += await CallbackFailureAsync(root);
            checks += await StartupAndCleanupAsync();
            return checks;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ProcessProfile Profile(string root, TimeSpan? timeout = null)
    {
        string executable = Environment.ProcessPath!;
        string[] arguments = Path.GetFileNameWithoutExtension(executable) == "dotnet"
            ? [typeof(LifecycleChecks).Assembly.Location, "--lifecycle-worker"] : ["--lifecycle-worker"];
        return new ProcessProfile(executable, arguments, trustedCode: true, workspaceRoot: root,
            timeout: timeout ?? TimeSpan.FromSeconds(10));
    }

    private static Task<IPluginSession> BindAsync(PluginHost host, ExecutionProfile profile, IHostCallbacks? callbacks = null) =>
        host.BindAsync(new PluginContext("tenant", "quality", "1", "test", Empty), profile,
            callbacks ?? new ThrowingCallbacks(), ["probe"]);

    private static async Task<int> SetupAsync(string root)
    {
        await using var host = new PluginHost();
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromDays(50), TimeSpan.FromMilliseconds(uint.MaxValue) })
        {
            await ExpectAsync<ArgumentOutOfRangeException>(() => BindAsync(host, Profile(root, timeout)));
        }
        Check(host.Snapshot.Bindings == 0, "invalid deadlines never register");
        await using var session = await BindAsync(host, Profile(root, TimeSpan.FromMilliseconds(uint.MaxValue - 1)));
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "WeavePort.Hosting",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => throw new ApplicationException("setup failure")
        })
        {
            ActivitySource.AddActivityListener(listener);
            await ExpectAsync<ApplicationException>(() => session.InvokeAsync("echo", Empty));
        }
        Check((await session.InvokeAsync("echo", Empty)).Status == "ok", "setup releases gate for subsequent invocation");
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Check(host.Snapshot.Bindings == 0 && host.Snapshot.Workers == 0, "setup failure permits disposal");
        return 7;
    }

    private static async Task<int> CancellationAsync(string root)
    {
        var log = new CapturingLogger();
        var host = new PluginHost(log);
        var callbacks = new CancellationCallbacks();
        var session = await BindAsync(host, Profile(root), callbacks);
        Task<InvocationResult> invocation = session.InvokeAsync("callback", Empty);
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ExpectAsync<AggregateException>(() => session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        Check((await invocation).Status == "disabled", "cancelled invocation disabled");
        Check(host.Snapshot.Bindings == 0 && host.Snapshot.Workers == 0 && host.Snapshot.Tenants == 1,
            "callback retains admission after binding and worker removal");
        callbacks.Release.TrySetResult();
        await callbacks.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 100 && host.Snapshot.Tenants != 0; i++) await Task.Delay(10);
        Check(host.Snapshot.Tenants == 0, "completed callback releases tenant");
        await host.DisposeAsync();
        Check(log.Events.Any(item => item.Id == 1004), "cancellation cleanup failure logged");
        log.AssertSafe();
        return 6;
    }

    private static async Task<int> CallbackFailureAsync(string root)
    {
        var log = new CapturingLogger();
        await using var host = new PluginHost(log);
        await using var session = await BindAsync(host, Profile(root));
        var result = await session.InvokeAsync("callback", JsonSerializer.SerializeToElement(Secret));
        Check(result.Status == "failed", "callback failure mapped");
        Check(log.Events.Any(item => item.Id == 1002) && log.Events.Any(item => item.Id == 1003), "callback and invocation stage logged");
        log.AssertSafe();
        return 3;
    }

    private static async Task<int> StartupAndCleanupAsync()
    {
        var log = new CapturingLogger();
        var host = new PluginHost(log, options: new WorkerPoolOptions(MaintenanceInterval: TimeSpan.FromMilliseconds(20)));
        var profile = new FailingProfile();
        await using var session = await BindAsync(host, profile);
        Check((await session.InvokeAsync("echo", Empty)).Status == "failed", "startup failure mapped");
        Check(host.Snapshot.Quarantined == 1, "uncertain cleanup retains reservation");
        for (int i = 0; i < 100 && !log.Events.Any(item => item.Id == 1005); i++) await Task.Delay(20);
        Check(log.Events.Any(item => item.Id == 1005), "maintenance failure logged");
        await ExpectAsync<AggregateException>(() => host.DisposeAsync().AsTask());
        Check(log.Events.Any(item => item.Id == 1001) && log.Events.Any(item => item.Id == 1004), "startup and cleanup stages logged");
        log.AssertSafe();
        return 6;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class ThrowingCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new IOException(Secret);
    }

    private sealed class CancellationCallbacks : IHostCallbacks
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            using var registration = token.Register(() => throw new InvalidOperationException(Secret));
            Entered.TrySetResult();
            try { await Release.Task; token.ThrowIfCancellationRequested(); return Empty; }
            finally { Completed.TrySetResult(); }
        }
    }

    private sealed record FailingProfile() : ExecutionProfile(256, TimeSpan.FromSeconds(5), null)
    {
        public override ExecutionProtection Protection => ExecutionProtection.None;
        internal override Task<ExecutionProfile> ResolveAsync(CancellationToken token) => Task.FromResult<ExecutionProfile>(this);
        internal override ExecutionProfile Normalize() => this;
        internal override Worker CreateWorker(string version, TimeProvider clock) => new FailingWorker(this, version);
    }

    private sealed class FailingWorker(ExecutionProfile profile, string version) : Worker(profile, version)
    {
        internal override Stream Input => Stream.Null;
        internal override bool Running => false;
        internal override Task StartAsync(CancellationToken token) => throw new IOException(Secret);
        internal override Task<JsonElement> DestroyAsync() => throw new IOException(Secret);
    }

    private sealed class CapturingLogger : ILogger<PluginHost>
    {
        internal ConcurrentQueue<(int Id, string Text)> Events { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            Check(error is null, "no exception objects exported");
            Events.Enqueue((id.Id, formatter(state, error)));
        }
        internal void AssertSafe() => Check(Events.All(item => !item.Text.Contains(Secret)), "sensitive content not logged");
    }
}
