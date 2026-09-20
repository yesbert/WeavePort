using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

if (args.Contains("--worker"))
{
    int gate = Array.IndexOf(args, "--ready-gate");
    if (gate >= 0)
    {
        await File.WriteAllTextAsync(args[gate + 1] + ".pending", Environment.ProcessId.ToString());
        File.Move(args[gate + 1] + ".pending", args[gate + 1] + ".entered", overwrite: true);
        while (!File.Exists(args[gate + 1])) await Task.Delay(10);
    }
    Console.WriteLine("{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}");
    int counter = 0;
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var doc = JsonDocument.Parse(line);
        var frame = doc.RootElement;
        string id = frame.GetProperty("id").GetString()!;
        string operation = frame.GetProperty("operation").GetString()!;
        if (operation == "hold")
        {
            Console.WriteLine(JsonSerializer.Serialize(new { type = "callback", id, callbackId = "1", operation = "hold", payload = new { } }));
            await Console.In.ReadLineAsync();
        }
        if (operation == "hang") await Task.Delay(Timeout.Infinite);
        if (operation == "crash") Environment.Exit(17);
        object value = operation == "payload" ? frame.GetProperty("payload") :
            new { tenant = frame.GetProperty("context").GetProperty("tenant").GetString(), counter = ++counter, profile = args.Contains("--alternate") ? "alternate" : "default" };
        Console.WriteLine(JsonSerializer.Serialize(new { type = "result", id, value }));
    }
    return;
}
await SchedulingChecks.RunAsync();

internal static class SchedulingChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static int _checks;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "wp-scheduling-" + Guid.NewGuid().ToString("N"));
    private static readonly ProcessProfile Profile = new(Environment.ProcessPath!, [typeof(SchedulingChecks).Assembly.Location, "--worker"],
        trustedCode: true, workspaceRoot: Root, reservedMemoryMiB: 64);
    private static SchedulingOptions Options(int workers = 4) => new()
    {
        MaximumWorkers = workers, MaximumHeavyCalls = workers > 1 ? 1 : 0, MemoryBudgetMiB = workers * 64,
        MaximumPristineWorkers = 0, NormalTimeout = TimeSpan.FromSeconds(5), IdleTimeout = TimeSpan.FromSeconds(30)
    };
    private static Task<ScheduledPlugin> Bind(ScheduledPluginHost host, string tenant, string plugin = "one", IHostCallbacks? callbacks = null,
        PluginWorkClass workClass = PluginWorkClass.Normal) => host.RegisterAsync(new(tenant, plugin, "1", "test", Empty), Profile,
            callbacks ?? new Holds(), ["hold"], workClass);

    internal static async Task RunAsync()
    {
        await Serialization();
        await FairBorrowing();
        await HeavyAndQueue();
        await ReplacementAndDeadline();
        await CancellationAndShutdown();
        await DynamicReserve();
        await PristineAdmissionChecks.RunAsync(Root, Check);
        await PayloadAndAuthority();
        await MemoryAndIdle();
        await MemoryLedGrowth();
        await DormantRegistry();
        await CleanupFailure();
        Check(!Directory.Exists(Root) || !Directory.EnumerateFileSystemEntries(Root).Any(), "all worker workspaces removed");
        if (Directory.Exists(Root)) Directory.Delete(Root);
        Console.WriteLine($"PASS {_checks} public scheduler assertions");
    }

    private static async Task DormantRegistry()
    {
        await using var host = new ScheduledPluginHost(Options());
        var dormant = await Task.WhenAll(Enumerable.Range(0, 1024).Select(i => Bind(host, "dormant-" + i)));
        var holds = new Holds();
        var active = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Bind(host, "active-" + i, callbacks: holds)));
        var calls = active.Select(p => p.InvokeAsync("hold", Empty)).ToArray();
        await Until(() => holds.Entered.Count == 4);
        await Task.WhenAll(dormant.Select(p => p.DisposeAsync().AsTask()));
        Check(host.Snapshot is { Registrations: 4, Active: 4 } && host.Snapshot.Runtime.Workers == 4,
            "removing dormant registrations preserves active customer work");
        holds.ReleaseAll();
        Check((await Task.WhenAll(calls)).All(r => r.Status == "ok"), "active work completes after registry churn");
        await Until(() => host.Snapshot.Active == 0);
        await active[0].RestartAsync();
        Check(host.Snapshot.Runtime.Workers == 3 && host.Snapshot.Active == 0, "restart releases only the selected resident");
        Check((await active[0].InvokeAsync("echo", Empty)).Status == "ok", "restarted resident returns to service");
        await active[1].DisposeAsync();
        var replacement = await Bind(host, "active-1");
        Check((await replacement.InvokeAsync("echo", Empty)).Status == "ok" && host.Snapshot.Runtime.Workers == 4,
            "last tenant registration can be removed and registered again after churn");
    }

    private static async Task MemoryLedGrowth()
    {
        await using var host = new ScheduledPluginHost(Options() with
        {
            MaximumWorkers = null, MemoryBudgetMiB = 40 * 64, MaximumConcurrentStarts = 1,
            NormalTimeout = TimeSpan.FromSeconds(20), IdleTimeout = TimeSpan.FromMinutes(2)
        });
        var plugins = await Task.WhenAll(Enumerable.Range(0, 41).Select(i => Bind(host, "growth-" + i)));
        var results = await Task.WhenAll(plugins.Take(40).Select(p => p.InvokeAsync("echo", Empty)));
        Check(results.All(r => r.Status == "ok"), "start queue drains without concurrent-start rejection");
        Check(host.Snapshot.Runtime.Workers == 40, "memory-led pool grows beyond 32 with one concurrent start");
        Check((await plugins[40].InvokeAsync("echo", Empty)).Status == "ok", "new tenant admitted at memory ceiling");
        Check(host.Snapshot.Runtime.Workers == 40 && host.Snapshot.Evictions == 1, "memory pressure evicts only needed idle worker");
    }

    private static async Task Serialization()
    {
        await using var host = new ScheduledPluginHost(Options());
        var a = await Bind(host, "a");
        await Throws<NotSupportedException>(() => a.InvokeAsync("$sdk.start", Empty));
        Check(host.Snapshot.Runtime.Workers == 0, "unsupported stateful SDK stream rejected before dispatch");
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => a.InvokeAsync("echo", Empty)));
        Check(results.All(r => r.Status == "ok"), "same binding queues without busy");
        Check(results.Select(r => r.Instance).Distinct().Count() == 1, "one worker for same plugin");
        Check(results.Select(r => r.Value.GetProperty("counter").GetInt32()).SequenceEqual(Enumerable.Range(1, 12)), "per-plugin FIFO");
        await Throws<InvalidOperationException>(() => Bind(host, "a"));
        Check(host.Snapshot.Runtime.Workers == 1 && host.Snapshot.Runtime.Bindings == 1, "duplicate registration cleanup");
        await a.DisposeAsync();
        Check(host.Snapshot.Registrations == 0 && host.Snapshot.Runtime.Workers == 0, "explicit disposal removes registration");
    }

    private static async Task FairBorrowing()
    {
        await using var host = new ScheduledPluginHost(Options());
        var holds = new Holds();
        var a = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Bind(host, "a", "p" + i, holds)));
        var running = a.Take(4).Select(p => p.InvokeAsync("hold", Empty)).ToArray();
        await Until(() => holds.Entered.Count == 4);
        Check(host.Snapshot.Active == 4, "single tenant borrows all free workers across plugins");
        var waitingA = a[4].InvokeAsync("hold", Empty);
        var bHolds = new Holds();
        var b = await Bind(host, "b", callbacks: bHolds);
        var waitingB = b.InvokeAsync("hold", Empty);
        holds.ReleaseOne();
        await Until(() => bHolds.Entered.Count == 1);
        Check(holds.Entered.Count == 3 && !waitingA.IsCompleted, "new tenant gets next slot before saturated tenant");
        bHolds.ReleaseAll();
        Check((await waitingB).Value.GetProperty("tenant").GetString() == "b", "replacement retains tenant identity");
        holds.AutoRelease = true;
        holds.ReleaseAll();
        Check((await Task.WhenAll(running.Append(waitingA))).All(r => r.Status == "ok"), "all borrowed work completes");
        Check(host.Snapshot.Evictions >= 1 && host.Snapshot.Runtime.Workers <= 4, "pressure eviction honors shared cap");
    }

    private static async Task HeavyAndQueue()
    {
        await using var host = new ScheduledPluginHost(Options() with { MaximumQueuedCallsPerTenant = 1, QueueTimeout = TimeSpan.FromMilliseconds(200) });
        var holds = new Holds();
        var heavy = await Bind(host, "a", "heavy", holds, PluginWorkClass.Heavy);
        var next = await Bind(host, "b", "heavy", workClass: PluginWorkClass.Heavy);
        var first = heavy.InvokeAsync("hold", Empty);
        await Until(() => holds.Entered.Count == 1);
        var pending = next.InvokeAsync("echo", Empty);
        var overflow = await next.InvokeAsync("echo", Empty);
        Check(overflow.Status == "busy" && !overflow.MayHaveExecuted, "bounded queue rejects before dispatch");
        var normal = await Bind(host, "normal");
        Check((await normal.InvokeAsync("echo", Empty)).Status == "ok", "normal work proceeds beside heavy work");
        Check((await pending).Status == "busy" && next.Instance == "", "queue deadline does not dispatch heavy work");
        await Bind(host, "a", "heavy2", workClass: PluginWorkClass.Heavy);
        await Throws<InvalidOperationException>(() => Bind(host, "a", "heavy3", workClass: PluginWorkClass.Heavy));
        holds.ReleaseAll();
        Check((await first).Status == "ok", "heavy completes within execution budget");
    }

    private static async Task ReplacementAndDeadline()
    {
        await using var host = new ScheduledPluginHost(Options(1) with { NormalTimeout = TimeSpan.FromMilliseconds(500) });
        var a = await Bind(host, "a");
        var b = await Bind(host, "b");
        Check((await a.InvokeAsync("echo", Empty)).Status == "ok", "first customer starts");
        var old = a.Instance;
        Check((await b.InvokeAsync("echo", Empty)).Value.GetProperty("counter").GetInt32() == 1, "new customer starts clean");
        Check((await a.InvokeAsync("echo", Empty)).Value.GetProperty("counter").GetInt32() == 1 && a.Instance != old, "returning customer reconstructs state");
        var expired = await a.InvokeMeasuredAsync("hang", Empty);
        Check(expired.Result.Status == "timeout" && expired.Result.MayHaveExecuted, "normal deadline terminates worker");
        Check(host.Snapshot.Runtime.Workers == 0, "deadline removes worker reservation");
        Check((await b.InvokeAsync("echo", Empty)).Status == "ok", "another tenant proceeds after timeout");
        Check((await b.InvokeAsync("crash", Empty)).Status == "failed", "crash reported");
        Check((await b.InvokeAsync("echo", Empty)).Status == "ok", "crash releases capacity for replacement");
    }

    private static async Task CancellationAndShutdown()
    {
        var host = new ScheduledPluginHost(Options(1));
        var holds = new Holds();
        var a = await Bind(host, "a", callbacks: holds);
        var active = a.InvokeAsync("hold", Empty);
        await Until(() => holds.Entered.Count == 1);
        using var cancel = new CancellationTokenSource();
        var queued = a.InvokeAsync("echo", Empty, cancel.Token);
        cancel.Cancel();
        Check((await queued).Status == "cancelled", "queued cancellation never dispatches");
        var disabled = a.InvokeAsync("echo", Empty);
        await host.DisposeAsync();
        await host.DisposeAsync();
        Check((await disabled).Status == "disabled", "shutdown completes queued calls");
        Check((await active).Status is "cancelled" or "disabled", "shutdown cancels active call");
        Check(host.Snapshot.Runtime is { Workers: 0, Bindings: 0, Tenants: 0 }, "shutdown drains workers and bindings");
    }

    private static async Task DynamicReserve()
    {
        await using var host = new ScheduledPluginHost(Options() with
        { MaximumPristineWorkers = 2, IdleTimeout = TimeSpan.FromMilliseconds(300), DemandWindow = TimeSpan.FromSeconds(2) });
        var a = await Bind(host, "a");
        await Task.Delay(150);
        Check(host.Snapshot.Runtime.Workers == 0, "registration alone starts no speculative worker");
        Check((await a.InvokeAsync("echo", Empty)).Status == "ok", "demand starts registered profile");
        await Until(() => host.Snapshot.Runtime.Pristine == 1);
        Check(host.Snapshot.Runtime.Pristine == 1, "one demanded profile receives shared reserve, not per tenant");
        await a.DisposeAsync();
        await Until(() => host.Snapshot.Runtime.Workers == 0);
        Check(host.Snapshot.Runtime.Workers == 0, "unregistered profile reserve removed");
    }

    private static async Task PayloadAndAuthority()
    {
        await using var host = new ScheduledPluginHost(Options(1) with { MaximumPayloadBytes = 64 });
        var holds = new Holds();
        var a = await Bind(host, "a", callbacks: holds);
        var first = a.InvokeAsync("hold", Empty);
        await Until(() => holds.Entered.Count == 1);
        Task<InvocationResult> pending;
        using (var document = JsonDocument.Parse("{\"text\":\"owned\"}")) pending = a.InvokeAsync("payload", document.RootElement);
        await Throws<InvalidDataException>(() => a.InvokeAsync("payload", JsonSerializer.SerializeToElement(new string('x', 65))));
        holds.ReleaseAll();
        await first;
        Check((await pending).Value.GetProperty("text").GetString() == "owned", "queued payload outlives caller document");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Throws<OperationCanceledException>(() => a.RestartAsync(cancel.Token));
        Check(host.Snapshot.Failure is null && (await a.InvokeAsync("echo", Empty)).Status == "ok", "cancelled restart does not poison admission");
        var nested = new Nested(a);
        var b = await Bind(host, "b", callbacks: nested);
        Check((await b.InvokeAsync("hold", Empty)).Status == "ok" && nested.Status == "denied", "nested scheduled callback denied without deadlock");
    }

    private static async Task MemoryAndIdle()
    {
        await using var host = new ScheduledPluginHost(Options() with { MemoryBudgetMiB = 128, IdleTimeout = TimeSpan.FromMilliseconds(200) });
        var holds = new Holds();
        var plugins = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => Bind(host, "t" + i, callbacks: holds)));
        var calls = plugins.Select(p => p.InvokeAsync("hold", Empty)).ToArray();
        await Until(() => holds.Entered.Count == 2);
        Check(host.Snapshot.Runtime is { Workers: 2, ReservedMemoryMiB: 128 }, "memory budget limits active work before count budget");
        holds.AutoRelease = true;
        holds.ReleaseAll();
        Check((await Task.WhenAll(calls)).All(r => r.Status == "ok"), "memory-constrained queued work completes");
        await Until(() => host.Snapshot.Runtime.Workers == 0);
        Check(host.Snapshot.Registrations == 3, "idle expiry keeps registrations without resident processes");
    }

    private static async Task CleanupFailure()
    {
        await using var host = new ScheduledPluginHost(Options());
        var callbacks = new ThrowOnCancel();
        var a = await Bind(host, "a", callbacks: callbacks);
        var b = await Bind(host, "b");
        var active = a.InvokeAsync("hold", Empty);
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Throws<AggregateException>(() => a.DisposeAsync().AsTask());
        Check((await active).Status == "disabled", "cleanup exception still cancels active invocation");
        Check(host.Snapshot.Failure is not null && (await b.InvokeAsync("echo", Empty)).Status == "busy", "cleanup failure closes new scheduler admission");
        await Until(() => host.Snapshot.Runtime.Tenants == 1);
        Check(host.Snapshot.Runtime.Workers == 0, "independent worker cleanup completes despite cancellation exception");
    }

    private sealed class Nested(ScheduledPlugin target) : IHostCallbacks
    {
        internal string? Status;
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        { Status = (await target.InvokeAsync("echo", Empty, token)).Status; return Empty; }
    }
    private sealed class ThrowOnCancel : IHostCallbacks
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            using var registration = token.Register(() => throw new InvalidOperationException("fixture-cancellation-failure"));
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Empty;
        }
    }

    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { _checks++; return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Until(Func<bool> condition)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); while (!condition()) await Task.Delay(10, timeout.Token); }

    private sealed class Holds : IHostCallbacks
    {
        internal System.Collections.Concurrent.ConcurrentQueue<TaskCompletionSource> Entered { get; } = new();
        internal volatile bool AutoRelease;
        public async ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            if (AutoRelease) return Empty;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Entered.Enqueue(gate);
            await gate.Task.WaitAsync(token);
            return Empty;
        }
        internal void ReleaseOne() { if (Entered.TryDequeue(out var gate)) gate.TrySetResult(); }
        internal void ReleaseAll() { while (Entered.TryDequeue(out var gate)) gate.TrySetResult(); }
    }
}
