using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static partial class SharedChecks
{
    private static async Task CancellationGraceAsync(ProcessProfile profile)
    {
        await using var host = Host();
        var callbacks = new Callbacks();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 2,
            CancellationGrace = TimeSpan.FromMilliseconds(50)
        }, callbacks, ["entered"]);
        await using var a = shared.For("A");
        await using var b = shared.For("B");
        using var stop = new CancellationTokenSource();
        Task<JsonElement> pending = a.CallAsync("delay", Json(new
        {
            milliseconds = 2000,
            announce = true
        }), stop.Token);
        Task<JsonElement> other = b.CallAsync("delay", Json(new
        {
            milliseconds = 2000
        }));
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel();
        Check(await OutcomeAsync(pending) == "cancelled", "Abandoned caller did not return cancellation.");
        Check(await OutcomeAsync(other) == "failed", "Cancellation grace did not retire stuck worker.");
        await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Cancellation retirement did not replace worker.");
    }

    private static async Task CleanupFailureAsync(ProcessProfile profile)
    {
        await using var host = Host();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 2
        }, new Callbacks(), []);
        await using var a = shared.For("A");
        await using var b = shared.For("B");
        Task<JsonElement> pending = a.CallAsync("delay", Json(new
        {
            milliseconds = 2000
        }));
        await WaitAsync(() => shared.Snapshot.ActiveCalls == 1, "Peer call not admitted.");
        Check(await OutcomeAsync(b.CallAsync("cleanupFail", Json(new
        {
        }))) == "failed", "Cleanup failure must fail the call.");
        Check(await OutcomeAsync(pending) == "failed", "Cleanup failure did not retire entire channel.");
        await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Cleanup-failed worker did not restart.");
    }

    private static async Task SilenceAsync(ProcessProfile profile)
    {
        await using var host = Host();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 1,
            SilenceTimeout = TimeSpan.FromMilliseconds(100)
        }, new Callbacks(), []);
        await using var client = shared.For("A");
        Check(await OutcomeAsync(client.CallAsync("delay", Json(new
        {
            milliseconds = 2000
        }))) == "failed", "Bounded silence must retire an unresponsive channel.");
        await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == 1, "Silent channel did not restart.");
    }

    private static async Task MalformedAsync(ProcessProfile profile)
    {
        await using var host = Host();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 1
        }, new Callbacks(), []);
        await using var client = shared.For("A");
        for (int i = 0; i < 2; i++)
        {
            Check(await OutcomeAsync(client.CallAsync("malformed", Json(new
            {
                invalidJson = i == 0
            }))) == "failed", "Malformed JSON or unknown identity must fail the channel.");
            int restarts = i + 1;
            await WaitAsync(() => shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.Restarts == restarts, "Malformed channel did not restart.");
        }
    }

    private static async Task BlockedWriterAsync(string root, string python)
    {
        await using var host = Host();
        var profile = new ProcessProfile(python, [Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures/blocked.py")], trustedCode: true, reservedMemoryMiB: 128, timeout: TimeSpan.FromSeconds(30)) { ReusePolicy = WorkerReusePolicy.Shared };
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 1,
            CancellationGrace = TimeSpan.FromMilliseconds(200),
            SilenceTimeout = TimeSpan.FromMinutes(1),
            MaximumRestarts = 0
        }, new Callbacks(), []);
        await using var client = shared.For("A");
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var watch = Stopwatch.StartNew();
        string outcome = await OutcomeAsync(client.CallAsync("echo", Json(new
        {
            data = new string('x', 500 * 1024)
        }), stop.Token)).WaitAsync(TimeSpan.FromSeconds(3));
        Check(outcome == "cancelled" && watch.Elapsed < TimeSpan.FromSeconds(2), "Blocked pipe write prevented prompt cancellation.");
        await WaitAsync(() => shared.Snapshot.Disabled, "Partially written channel was not retired.");
    }

}
