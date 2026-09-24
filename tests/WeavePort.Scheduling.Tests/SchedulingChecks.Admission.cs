using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class SchedulingChecks
{
    private static async Task SerializationAsync()
    {
        await using var host = new PluginHost(Options());
        var a = await BindAsync(host, "a");
        await ThrowsAsync<NotSupportedException>(() => a.InvokeAsync("$sdk.start", Empty));
        Check(host.Snapshot.Workers == 0, "unsupported stateful SDK stream rejected before dispatch");
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => a.InvokeAsync("echo", Empty)));
        Check(results.All(r => r.Status == "ok"), "same binding queues without busy");
        Check(results.Select(r => r.Instance).Distinct().Count() == 1, "one worker for same plugin");
        Check(results.Select(r => r.Value.GetProperty("counter").GetInt32()).SequenceEqual(Enumerable.Range(1, 12)), "per-plugin FIFO");
        await ThrowsAsync<InvalidOperationException>(() => BindAsync(host, "a"));
        Check(host.Snapshot.Workers == 1 && host.Snapshot.Bindings == 1, "duplicate registration cleanup");
        await a.DisposeAsync();
        Check(host.Scheduling!.Registrations == 0 && host.Snapshot.Workers == 0, "explicit disposal removes registration");
    }

    private static async Task FairBorrowingAsync()
    {
        await using var host = new PluginHost(Options());
        var holds = new Holds();
        var a = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => BindAsync(host, "a", "p" + i, holds)));
        var running = a.Take(4).Select(p => p.InvokeAsync("hold", Empty)).ToArray();
        await UntilAsync(() => holds.Entered.Count == 4);
        Check(host.Scheduling!.Active == 4, "single tenant borrows all free workers across plugins");
        var waitingA = a[4].InvokeAsync("hold", Empty);
        var bHolds = new Holds();
        var b = await BindAsync(host, "b", callbacks: bHolds);
        var waitingB = b.InvokeAsync("hold", Empty);
        holds.ReleaseOne();
        await UntilAsync(() => bHolds.Entered.Count == 1);
        Check(holds.Entered.Count == 3 && !waitingA.IsCompleted, "new tenant gets next slot before saturated tenant");
        bHolds.ReleaseAll();
        Check((await waitingB).Value.GetProperty("tenant").GetString() == "b", "replacement retains tenant identity");
        holds.AutoRelease = true;
        holds.ReleaseAll();
        Check((await Task.WhenAll(running.Append(waitingA))).All(r => r.Status == "ok"), "all borrowed work completes");
        Check(host.Scheduling!.Evictions >= 1 && host.Snapshot.Workers <= 4, "pressure eviction honors shared cap");
    }

    private static async Task HeavyAndQueueAsync()
    {
        await using var host = new PluginHost(Options() with
        {
            MaximumQueuedCallsPerTenant = 1,
            QueueTimeout = TimeSpan.FromMilliseconds(200)
        });
        var holds = new Holds();
        var heavy = await BindAsync(host, "a", "heavy", holds, PluginWorkClass.Heavy);
        var next = await BindAsync(host, "b", "heavy", workClass: PluginWorkClass.Heavy);
        var first = heavy.InvokeAsync("hold", Empty);
        await UntilAsync(() => holds.Entered.Count == 1);
        var pending = next.InvokeAsync("echo", Empty);
        var overflow = await next.InvokeAsync("echo", Empty);
        Check(overflow.Status == "busy" && !overflow.MayHaveExecuted, "bounded queue rejects before dispatch");
        var normal = await BindAsync(host, "normal");
        Check((await normal.InvokeAsync("echo", Empty)).Status == "ok", "normal work proceeds beside heavy work");
        Check((await pending).Status == "busy" && next.Instance == "", "queue deadline does not dispatch heavy work");
        await BindAsync(host, "a", "heavy2", workClass: PluginWorkClass.Heavy);
        await ThrowsAsync<InvalidOperationException>(() => BindAsync(host, "a", "heavy3", workClass: PluginWorkClass.Heavy));
        holds.ReleaseAll();
        Check((await first).Status == "ok", "heavy completes within execution budget");
    }

    private static async Task DynamicReserveAsync()
    {
        await using var host = new PluginHost(Options() with
        {
            MaximumPristineWorkers = 2,
            IdleTimeout = TimeSpan.FromMilliseconds(300),
            DemandWindow = TimeSpan.FromSeconds(2)
        });
        var a = await BindAsync(host, "a");
        await Task.Delay(150);
        Check(host.Snapshot.Workers == 0, "registration alone starts no speculative worker");
        Check((await a.InvokeAsync("echo", Empty)).Status == "ok", "demand starts registered profile");
        await UntilAsync(() => host.Snapshot.Pristine == 1);
        Check(host.Snapshot.Pristine == 1, "one demanded profile receives shared reserve, not per tenant");
        await a.DisposeAsync();
        await UntilAsync(() => host.Snapshot.Workers == 0);
        Check(host.Snapshot.Workers == 0, "unregistered profile reserve removed");
    }

}
