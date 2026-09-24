using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static partial class SharedChecks
{
    private static async Task OverlapAsync(ProcessProfile profile)
    {
        await using var host = Host();
        var callbacks = new Callbacks(16);
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 16
        }, callbacks, ["barrier"]);
        var clients = Enumerable.Range(0, 16).Select(i => shared.For($"tenant-{i}")).ToArray();
        var calls = clients.Select((client, i) => client.CallAsync("barrier", Json(new { index = i }))).ToArray();
        JsonElement[] results;
        try
        {
            results = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch { Console.WriteLine(Json(new { shared = shared.Snapshot, scheduling = host.Scheduling, callbacks.MaximumActive, statuses = calls.Select(c => c.Status.ToString()) })); throw; }
        for (int i = 0; i < results.Length; i++)
        {
            Check(results[i].GetProperty("tenant").GetString() == $"tenant-{i}", "Invocation tenant crossed.");
            Check(results[i].GetProperty("callback").GetProperty("tenant").GetString() == $"tenant-{i}", "Callback tenant crossed.");
        }
        Check(callbacks.MaximumActive == 16, "All sixteen handlers must overlap at a functional barrier.");
        Check(shared.Snapshot.ReadyWorkers == 1 && shared.Snapshot.ActiveCalls == 0, "Single resident worker accounting.");
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }

    private static async Task CallbacksAndErrorsAsync(ProcessProfile profile)
    {
        await using var host = Host();
        var callbacks = new Callbacks();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 4
        }, callbacks, ["echo", "throw"]);
        await using var a = shared.For("A");
        await using var b = shared.For("B");
        string instance = (await a.CallAsync("echo", Json(new
        {
        }))).GetProperty("pid").ToString();
        Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new
        {
            count = 4
        }))) == "failed", "Per-invocation callback limit.");
        Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new
        {
            operation = "denied"
        }))) == "failed", "Denied callback must fail only its invocation.");
        Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new
        {
            operation = "throw"
        }))) == "failed", "Host callback exception must fail only its invocation.");
        Check(await OutcomeAsync(a.CallAsync("fail", Json(new
        {
        }))) == "failed", "Author failure must surface.");
        JsonElement result = await b.CallAsync("callbacks", Json(new
        {
            count = 3
        }));
        Check(result.GetProperty("callback").GetProperty("tenant").GetString() == "B", "Callback budget reset per invocation.");
        Check((await b.CallAsync("echo", Json(new
        {
        }))).GetProperty("pid").ToString() == instance, "Ordinary failures retired healthy worker.");
        Check(shared.Snapshot.Restarts == 0, "Isolated errors unexpectedly restarted process.");
    }

    private static async Task LargerCallbackBudgetAsync(ProcessProfile profile)
    {
        await using var host = Host();
        await using var shared = await host.ShareAsync(Context(), profile with
        {
            MaximumCallbacks = 64
        }, new()
        {
            Degree = 2
        }, new Callbacks(), ["echo"]);
        await using var client = shared.For("A");
        Check(await OutcomeAsync(client.CallAsync("callbacks", Json(new
        {
            count = 40
        }))) == "ok", "Operator-approved callback budget of 64 must support 40 callbacks.");
        await using var defaults = await host.ShareAsync(Context(), profile with
        {
            MaximumCallbacks = 8
        }, new()
        {
            Degree = 2
        }, new Callbacks(), ["echo"]);
        await using var limited = defaults.For("B");
        Check(await OutcomeAsync(limited.CallAsync("callbacks", Json(new
        {
            count = 9
        }))) == "failed", "Default callback budget must reject callback nine.");
    }

    private static async Task DetachedCallbackCapacityAsync(ProcessProfile profile)
    {
        await using var host = new PluginHost(new SchedulingOptions { MaximumCallsPerTenant = 1, MaximumWorkers = 2, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512 });
        var callbacks = new RetainedCallbacks();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 2
        }, callbacks, ["block", "echo"]);
        await using var a = shared.For("A");
        await using var b = shared.For("B");
        using var stop = new CancellationTokenSource();
        Task<JsonElement> pending = a.CallAsync("callbacks", Json(new
        {
            operation = "block"
        }), stop.Token);
        await callbacks.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        stop.Cancel();
        Check(await OutcomeAsync(pending) == "cancelled", "Blocked callback did not cancel caller.");
        await WaitAsync(() => shared.Snapshot.ActiveCalls == 0, "Cancelled callback did not terminate SDK invocation.");
        try
        {
            Check(await OutcomeAsync(a.CallAsync("callbacks", Json(new
            {
            }))) == "failed", "Detached callback released tenant callback capacity early.");
            Check(await OutcomeAsync(b.CallAsync("callbacks", Json(new
            {
            }))) == "ok", "Detached tenant callback blocked another tenant.");
        }
        finally { callbacks.Release.TrySetResult(); }
    }

}
