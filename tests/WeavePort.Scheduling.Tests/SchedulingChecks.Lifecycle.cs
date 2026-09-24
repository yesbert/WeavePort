using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class SchedulingChecks
{
    private static async Task ReplacementAndDeadlineAsync()
    {
        await using var host = new PluginHost(Options(1) with
        {
            NormalTimeout = TimeSpan.FromMilliseconds(500)
        });
        var a = await BindAsync(host, "a");
        var b = await BindAsync(host, "b");
        Check((await a.InvokeAsync("echo", Empty)).Status == "ok", "first customer starts");
        var old = a.Instance;
        Check((await b.InvokeAsync("echo", Empty)).Value.GetProperty("counter").GetInt32() == 1, "new customer starts clean");
        Check((await a.InvokeAsync("echo", Empty)).Value.GetProperty("counter").GetInt32() == 1 && a.Instance != old, "returning customer reconstructs state");
        var expired = await a.InvokeMeasuredAsync("hang", Empty);
        Check(expired.Result.Status == "timeout" && expired.Result.MayHaveExecuted, "normal deadline terminates worker");
        Check(host.Snapshot.Workers == 0, "deadline removes worker reservation");
        Check((await b.InvokeAsync("echo", Empty)).Status == "ok", "another tenant proceeds after timeout");
        Check((await b.InvokeAsync("crash", Empty)).Status == "failed", "crash reported");
        Check((await b.InvokeAsync("echo", Empty)).Status == "ok", "crash releases capacity for replacement");
    }

    private static async Task CancellationAndShutdownAsync()
    {
        var host = new PluginHost(Options(1));
        var holds = new Holds();
        var a = await BindAsync(host, "a", callbacks: holds);
        var active = a.InvokeAsync("hold", Empty);
        await UntilAsync(() => holds.Entered.Count == 1);
        using var cancel = new CancellationTokenSource();
        var queued = a.InvokeAsync("echo", Empty, cancel.Token);
        cancel.Cancel();
        Check((await queued).Status == "cancelled", "queued cancellation never dispatches");
        var disabled = a.InvokeAsync("echo", Empty);
        await host.DisposeAsync();
        await host.DisposeAsync();
        Check((await disabled).Status == "disabled", "shutdown completes queued calls");
        Check((await active).Status is "cancelled" or "disabled", "shutdown cancels active call");
        Check(host.Snapshot is { Workers: 0, Bindings: 0, Tenants: 0 }, "shutdown drains workers and bindings");
    }

    private static async Task MemoryAndIdleAsync()
    {
        await using var host = new PluginHost(Options() with
        {
            MemoryBudgetMiB = 128,
            IdleTimeout = TimeSpan.FromMilliseconds(200)
        });
        var holds = new Holds();
        var plugins = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => BindAsync(host, "t" + i, callbacks: holds)));
        var calls = plugins.Select(p => p.InvokeAsync("hold", Empty)).ToArray();
        await UntilAsync(() => holds.Entered.Count == 2);
        Check(host.Snapshot is { Workers: 2, ReservedMemoryMiB: 128 }, "memory budget limits active work before count budget");
        holds.AutoRelease = true;
        holds.ReleaseAll();
        Check((await Task.WhenAll(calls)).All(r => r.Status == "ok"), "memory-constrained queued work completes");
        await UntilAsync(() => host.Snapshot.Workers == 0);
        Check(host.Scheduling!.Registrations == 3, "idle expiry keeps registrations without resident processes");
    }

    private static async Task CleanupFailureAsync()
    {
        await using var host = new PluginHost(Options());
        var callbacks = new ThrowOnCancel();
        var a = await BindAsync(host, "a", callbacks: callbacks);
        var b = await BindAsync(host, "b");
        var active = a.InvokeAsync("hold", Empty);
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ThrowsAsync<AggregateException>(() => a.DisposeAsync().AsTask());
        Check((await active).Status == "disabled", "cleanup exception still cancels active invocation");
        Check(host.Scheduling!.Failure is not null && (await b.InvokeAsync("echo", Empty)).Status == "busy", "cleanup failure closes new scheduler admission");
        await UntilAsync(() => host.Snapshot.Tenants == 1);
        Check(host.Snapshot.Workers == 0, "independent worker cleanup completes despite cancellation exception");
    }

}
