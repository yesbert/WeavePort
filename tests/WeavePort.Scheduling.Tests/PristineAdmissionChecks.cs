using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class PristineAdmissionChecks
{
    internal static async Task RunAsync(string root, Action<bool, string> check)
    {
        await CheckAsync(root, check, wait: true, alternate: false, cancel: false);
        await CheckAsync(root, check, wait: true, alternate: true, cancel: false);
        await CheckAsync(root, check, wait: true, alternate: false, cancel: true);
        await CheckAsync(root, check, wait: false, alternate: false, cancel: false);
        await IndependentStartupAsync(root, check);
    }

    private static async Task CheckAsync(string root, Action<bool, string> check, bool wait, bool alternate, bool cancel)
    {
        Directory.CreateDirectory(root);
        string gate = Path.Combine(root, "ready-gate-" + Guid.NewGuid().ToString("N"));
        string[] arguments = [typeof(PristineAdmissionChecks).Assembly.Location, "--worker", "--ready-gate", gate];
        var warmProfile = new ProcessProfile(Environment.ProcessPath!, arguments, trustedCode: true, workspaceRoot: root, reservedMemoryMiB: 64);
        var callProfile = new ProcessProfile(Environment.ProcessPath!, alternate ? [.. arguments, "--alternate"] : arguments,
            trustedCode: true, workspaceRoot: root, reservedMemoryMiB: 64);
        await using var host = new PluginHost(1, new WorkerPoolOptions(MaximumWorkers: 1, MemoryBudgetMiB: 64,
            MaximumPristineWorkers: 1, MaximumConcurrentStarts: 2, MaximumWorkersPerTenant: 1, MemoryBudgetPerTenantMiB: 64,
            MaintenanceInterval: TimeSpan.FromMilliseconds(25)) { WaitForStartCapacity = wait });
        JsonElement empty = JsonSerializer.SerializeToElement(new { });
        var session = await host.BindAsync(new("reserve-customer", "plugin", "1", "test", empty), callProfile, new NoCallbacks(), []);
        Task warming = host.PrewarmAsync(warmProfile, "1", 1);
        Task<InvocationResult>? call = null;
        using var cancellation = new CancellationTokenSource();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(gate + ".entered")) await Task.Delay(10, timeout.Token);
            string warmPid = await File.ReadAllTextAsync(gate + ".entered");
            check(host.Snapshot.Starting == 1, "controlled pristine startup owns the only reservation");
            call = session.InvokeAsync("echo", empty, cancellation.Token);
            if (cancel)
            {
                cancellation.Cancel();
                check((await call.WaitAsync(TimeSpan.FromSeconds(2))).Status == "cancelled", "waiting for pristine capacity respects caller cancellation");
                check(host.Snapshot.Starting == 1, "caller cancellation does not destroy an unassigned shared startup");
            }
            else if (wait)
            {
                await Task.Delay(75);
                check(!call.IsCompleted, "foreground waits for a starting pristine worker instead of returning busy");
            }
            else
            {
                check((await call.WaitAsync(TimeSpan.FromSeconds(2))).Status == "busy", "default direct-host capacity admission remains fail-fast");
            }
            await File.WriteAllTextAsync(gate, "release");
            await warming.WaitAsync(TimeSpan.FromSeconds(5));
            InvocationResult result = wait && !cancel ? await call : await session.InvokeAsync("echo", empty);
            check(result.Status == "ok" && result.Value.GetProperty("tenant").GetString() == "reserve-customer",
                "customer completes after shared startup without losing identity");
            check(result.Value.GetProperty("profile").GetString() == (alternate ? "alternate" : "default"), "only a compatible pristine profile is adopted");
            check((await File.ReadAllTextAsync(gate + ".entered") == warmPid) != alternate,
                "matching startup is reused; incompatible startup is replaced within the budget");
            check(host.Snapshot is { Workers: 1, ReservedMemoryMiB: 64, Starting: 0 }, "foreground and replenishment do not oversubscribe the reservation");
        }
        finally
        {
            await File.WriteAllTextAsync(gate, "release");
            await warming.WaitAsync(TimeSpan.FromSeconds(5));
            if (call is not null) await call.WaitAsync(TimeSpan.FromSeconds(5));
            await host.DisposeAsync();
            File.Delete(gate);
            File.Delete(gate + ".entered");
            File.Delete(gate + ".pending");
        }
        check(host.Snapshot is { Workers: 0, Bindings: 0, Tenants: 0 }, "pristine admission control cleans all owned resources");
    }

    private static async Task IndependentStartupAsync(string root, Action<bool, string> check)
    {
        string warmGate = Path.Combine(root, "shared-" + Guid.NewGuid().ToString("N"));
        string otherGate = Path.Combine(root, "foreground-" + Guid.NewGuid().ToString("N"));
        ProcessProfile ProfileFor(string gate) => new(Environment.ProcessPath!,
            [typeof(PristineAdmissionChecks).Assembly.Location, "--worker", "--ready-gate", gate],
            trustedCode: true, workspaceRoot: root, reservedMemoryMiB: 64);
        await using var host = new PluginHost(2, new WorkerPoolOptions(MaximumWorkers: 2, MemoryBudgetMiB: 128,
            MaximumPristineWorkers: 1, MaximumConcurrentStarts: 3) { WaitForStartCapacity = true });
        JsonElement empty = JsonSerializer.SerializeToElement(new { });
        var a = await host.BindAsync(new("a", "plugin", "1", "test", empty), ProfileFor(otherGate), new NoCallbacks(), []);
        var b = await host.BindAsync(new("b", "plugin", "1", "test", empty), ProfileFor(warmGate), new NoCallbacks(), []);
        Task warming = host.PrewarmAsync(ProfileFor(warmGate), "1", 1);
        Task<InvocationResult>? first = null, second = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            while (!File.Exists(warmGate + ".entered")) await Task.Delay(10, timeout.Token);
            first = a.InvokeAsync("echo", empty);
            while (!File.Exists(otherGate + ".entered")) await Task.Delay(10, timeout.Token);
            second = b.InvokeAsync("echo", empty);
            await File.WriteAllTextAsync(warmGate, "release");
            var completed = await second.WaitAsync(TimeSpan.FromSeconds(1));
            check(completed.Status == "ok" && !first.IsCompleted, "shared startup completion is independent of unrelated slow foreground startup");
            await File.WriteAllTextAsync(otherGate, "release");
            check((await first).Status == "ok", "unrelated foreground startup also completes within its own deadline");
            check(host.Snapshot is { Workers: 2, ReservedMemoryMiB: 128 }, "independent startup wakeup preserves global accounting");
        }
        finally
        {
            await File.WriteAllTextAsync(warmGate, "release");
            await File.WriteAllTextAsync(otherGate, "release");
            await warming.WaitAsync(TimeSpan.FromSeconds(5));
            if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(5));
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(5));
            await host.DisposeAsync();
            foreach (string gate in new[] { warmGate, otherGate })
                foreach (string suffix in new[] { "", ".entered", ".pending" }) File.Delete(gate + suffix);
        }
    }

    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
