using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class SchedulingChecks
{
    private static async Task DormantRegistryAsync()
    {
        await using var host = new PluginHost(Options());
        var dormant = await Task.WhenAll(Enumerable.Range(0, 1024).Select(i => BindAsync(host, "dormant-" + i)));
        var holds = new Holds();
        var active = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => BindAsync(host, "active-" + i, callbacks: holds)));
        var calls = active.Select(p => p.InvokeAsync("hold", Empty)).ToArray();
        await UntilAsync(() => holds.Entered.Count == 4);
        await Task.WhenAll(dormant.Select(p => p.DisposeAsync().AsTask()));
        Check(host.Scheduling is { Registrations: 4, Active: 4 } && host.Snapshot.Workers == 4,
            "removing dormant registrations preserves active customer work");
        holds.ReleaseAll();
        Check((await Task.WhenAll(calls)).All(r => r.Status == "ok"), "active work completes after registry churn");
        await UntilAsync(() => host.Scheduling!.Active == 0);
        await active[0].RestartAsync();
        Check(host.Snapshot.Workers == 3 && host.Scheduling!.Active == 0, "restart releases only the selected resident");
        Check((await active[0].InvokeAsync("echo", Empty)).Status == "ok", "restarted resident returns to service");
        await active[1].DisposeAsync();
        var replacement = await BindAsync(host, "active-1");
        Check((await replacement.InvokeAsync("echo", Empty)).Status == "ok" && host.Snapshot.Workers == 4,
            "last tenant registration can be removed and registered again after churn");
    }

    private static async Task MemoryLedGrowthAsync()
    {
        await using var host = new PluginHost(Options() with
        {
            MaximumWorkers = null,
            MemoryBudgetMiB = 40 * 64,
            MaximumConcurrentStarts = 1,
            NormalTimeout = TimeSpan.FromSeconds(20),
            IdleTimeout = TimeSpan.FromMinutes(2)
        });
        var plugins = await Task.WhenAll(Enumerable.Range(0, 41).Select(i => BindAsync(host, "growth-" + i)));
        var results = await Task.WhenAll(plugins.Take(40).Select(p => p.InvokeAsync("echo", Empty)));
        Check(results.All(r => r.Status == "ok"), "start queue drains without concurrent-start rejection");
        Check(host.Snapshot.Workers == 40, "memory-led pool grows beyond 32 with one concurrent start");
        Check((await plugins[40].InvokeAsync("echo", Empty)).Status == "ok", "new tenant admitted at memory ceiling");
        Check(host.Snapshot.Workers == 40 && host.Scheduling!.Evictions == 1, "memory pressure evicts only needed idle worker");
    }

    private static async Task PayloadAndAuthorityAsync()
    {
        await using var host = new PluginHost(Options(1) with
        {
            MaximumPayloadBytes = 64
        });
        var holds = new Holds();
        var a = await BindAsync(host, "a", callbacks: holds);
        var first = a.InvokeAsync("hold", Empty);
        await UntilAsync(() => holds.Entered.Count == 1);
        Task<InvocationResult> pending;
        using (var document = JsonDocument.Parse("{\"text\":\"owned\"}"))
        {
            pending = a.InvokeAsync("payload", document.RootElement);
        }

        await ThrowsAsync<InvalidDataException>(() => a.InvokeAsync("payload", JsonSerializer.SerializeToElement(new string('x', 65))));
        holds.ReleaseAll();
        await first;
        Check((await pending).Value.GetProperty("text").GetString() == "owned", "queued payload outlives caller document");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => a.RestartAsync(cancel.Token));
        Check(host.Scheduling!.Failure is null && (await a.InvokeAsync("echo", Empty)).Status == "ok", "cancelled restart does not poison admission");
        var nested = new Nested(a);
        var b = await BindAsync(host, "b", callbacks: nested);
        Check((await b.InvokeAsync("hold", Empty)).Status == "ok" && nested.Status == "denied", "nested scheduled callback denied without deadlock");
    }

}
