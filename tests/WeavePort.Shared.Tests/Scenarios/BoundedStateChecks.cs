using System.Collections;
using System.Reflection;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Sdk.Client;

internal static class BoundedStateChecks
{
    internal static async Task RunAsync(string root, string python)
    {
        await CallbackAbuseAsync(root, python, "duplicate");
        await CallbackAbuseAsync(root, python, "flood");
        await TenantChurnAsync(root, python);
        Console.WriteLine("PASS: duplicate callback identity, bounded 10,000-frame flood and 512-tenant scheduler churn.");
    }

    private static PluginHost Host() => new(new SchedulingOptions { MaximumWorkers = 2, MaximumHeavyCalls = 0, MaximumPristineWorkers = 0, MemoryBudgetMiB = 512, MaximumCallsPerTenant = 16 });
    private static PluginContext Context() => new("operator", "bounded-test", "1", "test", JsonSerializer.SerializeToElement(new { }));
    private static void Check(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
    private static async Task CallbackAbuseAsync(string root, string python, string operation)
    {
        await using var host = Host();
        var profile = new ProcessProfile(python, [Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures/callback-abuse.py")], trustedCode: true, reservedMemoryMiB: 128) { ReusePolicy = WorkerReusePolicy.Shared, MaximumCallbacks = 8 };
        var callbacks = new CountingCallbacks();
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 2,
            MaximumRestarts = operation == "duplicate" ? 0 : 1
        }, callbacks, ["echo"]);
        await using var client = shared.For("abuse");
        string status;
        try
        {
            await client.CallAsync(operation, JsonSerializer.SerializeToElement(new
            {
            }));
            status = "ok";
        }
        catch (PluginCallException error) { status = error.Status; }
        Check(status == "failed", "Callback abuse must fail the invocation.");
        if (operation == "duplicate")
        {
            await WaitAsync(() => shared.Snapshot.Disabled);
            Check(shared.Snapshot.ReadyWorkers == 0, "Duplicate callback identity must retire the channel.");
        }
        else
        {
            Check(callbacks.Count <= 8, "Flood escaped the per-call callback budget.");
            Check(shared.Snapshot.Restarts == 0, "Over-budget invocation should not poison another call.");
            Check((await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
            {
            }))).GetString() == "abuse", "Callback flood corrupted the channel.");
            await WaitAsync(() => host.Snapshot.Tenants == 0);
        }
    }

    private static async Task TenantChurnAsync(string root, string python)
    {
        await using var host = Host();
        List<string> launch = [Path.Combine(root, "tests/WeavePort.Shared.Tests/fixtures/shared.py")];
        string? sdk = Environment.GetEnvironmentVariable("WP_SHARED_PYTHON_SDK");
        if (sdk is not null)
        {
            launch.AddRange(["--sdk-path", sdk]);
        }

        var profile = new ProcessProfile(python, launch, trustedCode: true, reservedMemoryMiB: 128) { ReusePolicy = WorkerReusePolicy.Shared };
        await using var shared = await host.ShareAsync(Context(), profile, new()
        {
            Degree = 16
        }, new CountingCallbacks(), []);
        for (int batch = 0; batch < 32; batch++)
        {
            await Task.WhenAll(Enumerable.Range(batch * 16, 16).Select(async index =>
            {
                await using var client = shared.For($"ephemeral-{index}");
                await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
                {
                }));
            }));
        }
        await WaitAsync(() => host.Snapshot.Tenants == 0 && host.Scheduling is { Active: 0, Queued: 0, Registrations: 0 });
        // Public counters establish quiescence; inspect the private fairness index to catch
        // retained tenant names even when all public admission records have been released.
        object scheduler = typeof(PluginHost).GetField("_scheduler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var turns = (IDictionary)scheduler.GetType().GetField("_turns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduler)!;
        Check(turns.Count == 0, "Completed transient tenants leaked fairness index entries.");
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class CountingCallbacks : IHostCallbacks
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new
            {
            }));
        }
    }
}
