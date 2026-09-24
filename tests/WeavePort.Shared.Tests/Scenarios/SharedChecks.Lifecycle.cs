using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static partial class SharedChecks
{
    private static async Task CancellationAsync(ProcessProfile profile)
    {
        await using var host = Host(true);
        var callbacks = new Callbacks();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 1,
            CancellationGrace = TimeSpan.FromSeconds(2)
        }, callbacks, ["entered"]);
        await using var a = shared.For("A");
        await using var b = shared.For("B");
        using var stop = new CancellationTokenSource();
        Task<JsonElement> pending = a.CallAsync("delay", Json(new
        {
            milliseconds = 350,
            announce = true
        }), stop.Token);
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel();
        Check(await OutcomeAsync(pending) == "cancelled", "Caller cancellation must return promptly.");
        Check(shared.Snapshot.ActiveCalls == 1 && shared.Snapshot.AbandonedCalls == 1, "Cancelled work released capacity before completion.");
        Check(await OutcomeAsync(b.CallAsync("echo", Json(new
        {
        }))) == "busy", "Retained slot admitted extra work.");
        await WaitAsync(() => shared.Snapshot.ActiveCalls == 0, "Cancelled slot did not clear after terminal.");
        Check(await OutcomeAsync(b.CallAsync("echo", Json(new
        {
        }))) == "ok", "Cancelled work poisoned worker.");
    }

    private static async Task CrashRecoveryAsync(ProcessProfile profile)
    {
        await using var host = Host();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 4,
            MaximumRestarts = 1
        }, new Callbacks(), []);
        await using var a = shared.For("A");
        await using var b = shared.For("B");
        Task<JsonElement> pending = a.CallAsync("delay", Json(new
        {
            milliseconds = 2000
        }));
        Task<JsonElement> crash = b.CallAsync("crash", Json(new
        {
            milliseconds = 100
        }));
        Check(await OutcomeAsync(crash) == "failed" && await OutcomeAsync(pending) == "failed", "Crash must fail all in-flight calls without replay.");
        await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Worker did not restart.");
        Check(await OutcomeAsync(a.CallAsync("echo", Json(new
        {
        }))) == "ok", "Restart did not restore residency.");
        Check(await OutcomeAsync(a.CallAsync("crash", Json(new
        {
        }))) == "failed", "Second crash must fail invocation.");
        await WaitAsync(() => shared.Snapshot.Disabled, "Restart budget was not enforced.");
        Check(shared.Snapshot.ReadyWorkers == 0, "Disabled worker appears ready.");
    }

    private static async Task OwnershipAndCoexistenceAsync(ProcessProfile profile)
    {
        await using var host = Host();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 2
        }, new Callbacks(), []);
        await using var client = shared.For("A");
        bool rejected = false;
        try
        {
            await foreach (var _ in client.StreamAsync("items", Json(new
            {
            })))
            {
            }
        }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Shared streaming must be rejected before dispatch.");
        rejected = false;
        try
        {
            await host.BindAsync(Context(), profile, new Callbacks(), []);
        }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Bind must reject shared ownership.");
        var serial = new ProcessProfile(profile.Executable, profile.Arguments.Append("--exclusive"), trustedCode: true, reservedMemoryMiB: 128);
        await using var binding = await host.BindAsync(Context(), serial, new Callbacks(), []);
        await using var exclusive = new LocalPluginClient(binding);
        Check(await OutcomeAsync(exclusive.CallAsync("echo", Json(new
        {
        }))) == "ok", "Exclusive binding failed alongside shared residency.");
        Check(await OutcomeAsync(client.CallAsync("echo", Json(new
        {
        }))) == "ok", "Exclusive binding damaged shared residency.");
        Check(host.Snapshot.Workers == 2, "Exclusive/shared worker budget is not unified.");
    }

    private static async Task ShutdownAsync(ProcessProfile profile)
    {
        var host = Host();
        var callbacks = new Callbacks();
        var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 2
        }, callbacks, ["entered"]);
        await using var client = shared.For("shutdown");
        Task<JsonElement> pending = client.CallAsync("delay", Json(new
        {
            milliseconds = 100,
            announce = true
        }));
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await host.DisposeAsync();
        Check(await OutcomeAsync(pending) is "ok" or "cancelled" or "failed", "Shutdown did not complete pending operation.");
        Check(host.Snapshot.Workers == 0, "Shutdown leaked process reservations.");
        await host.DisposeAsync();
        await shared.DisposeAsync();
    }

}
