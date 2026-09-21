using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class OperationLeaseChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    public static async Task RunAsync(string workspace)
    {
        await ResidencyAsync(workspace, failFast: false);
        await ResidencyAsync(workspace, failFast: true);
        await CancelledAdmissionAsync(workspace);
        Console.WriteLine("PASS operation residency under pressure, zero-wait refusal and cancelled lease admission");
    }

    private static PluginHost Host(bool failFast) => new(new SchedulingOptions
    {
        MaximumWorkers = 1, MemoryBudgetMiB = 64, MaximumPristineWorkers = 0,
        MaximumHeavyCalls = 0, QueueTimeout = failFast ? TimeSpan.Zero : TimeSpan.FromSeconds(5),
        IdleTimeout = TimeSpan.FromMilliseconds(50)
    });
    private static Task<IPluginSession> BindAsync(PluginHost host, string tenant, string workspace) => host.BindAsync(
        new PluginContext(tenant, "lease", "1", "test", Empty),
        new ProcessProfile(Environment.ProcessPath!, [typeof(OperationLeaseChecks).Assembly.Location, "--worker"],
            true, workspace, reservedMemoryMiB: 64) { Reconstructible = true }, new NoCallbacks(), []);

    private static async Task ResidencyAsync(string workspace, bool failFast)
    {
        await using var host = Host(failFast);
        await using var first = await BindAsync(host, "lease-a", workspace);
        await using var second = await BindAsync(host, "lease-b", workspace);
        await using var lease = await ((IPluginOperationSession)first).AcquireOperationAsync();
        InvocationResult started = await lease.InvokeAsync("echo", Empty);
        if (started.Status != "ok") throw new InvalidOperationException("Lease failed initial exchange.");
        Task<InvocationResult> pressure = second.InvokeAsync("echo", Empty);
        await Task.Delay(150);
        if (failFast)
        {
            if (!pressure.IsCompleted || (await pressure).Status != "busy") throw new InvalidOperationException("Zero wait queued instead of refusing.");
        }
        else if (pressure.IsCompleted) throw new InvalidOperationException("Pressure evicted an operation lease during consumer pause.");
        InvocationResult resumed = await lease.InvokeAsync("echo", Empty);
        if (resumed.Instance != started.Instance || resumed.Value.GetProperty("counter").GetInt32() != 2)
            throw new InvalidOperationException("Operation lost resident state between exchanges.");
        await lease.DisposeAsync();
        if (!failFast && (await pressure.WaitAsync(TimeSpan.FromSeconds(5))).Status != "ok")
            throw new InvalidOperationException("Releasing residency did not admit waiting tenant.");
    }

    private static async Task CancelledAdmissionAsync(string workspace)
    {
        await using var host = Host(failFast: true);
        await using var binding = await BindAsync(host, "cancel-lease", workspace);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            using var stop = new CancellationTokenSource();
            ValueTask<IPluginSession> acquisition = ((IPluginOperationSession)binding).AcquireOperationAsync(stop.Token);
            stop.Cancel();
            try { await using var lease = await acquisition; }
            catch (Exception error) when (error is OperationCanceledException or IOException) { }
            if (host.Scheduling!.Failure is not null) throw new InvalidOperationException("Lease cancellation poisoned scheduler: " + host.Scheduling.Failure);
            await Task.Delay(5);
        }
        if ((await binding.InvokeAsync("echo", Empty)).Status != "ok") throw new InvalidOperationException("Host unusable after cancelled lease admission.");
    }
    private sealed class NoCallbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
    }
}
