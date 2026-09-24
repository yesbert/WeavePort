using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static partial class SchedulingChecks
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private static int _checks;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "wp-scheduling-" + Guid.NewGuid().ToString("N"));
    private static readonly ProcessProfile Profile = new(Environment.ProcessPath!, [typeof(SchedulingChecks).Assembly.Location, "--worker"],
        trustedCode: true, workspaceRoot: Root, reservedMemoryMiB: 64)
    {
        Reconstructible = true
    };
    private static SchedulingOptions Options(int workers = 4) => new()
    {
        MaximumWorkers = workers,
        MaximumHeavyCalls = workers > 1 ? 1 : 0,
        MemoryBudgetMiB = workers * 64,
        MaximumPristineWorkers = 0,
        NormalTimeout = TimeSpan.FromSeconds(5),
        IdleTimeout = TimeSpan.FromSeconds(30)
    };
    private static async Task<ScheduledPlugin> BindAsync(PluginHost host, string tenant, string plugin = "one", IHostCallbacks? callbacks = null,
        PluginWorkClass workClass = PluginWorkClass.Normal) => (ScheduledPlugin)await host.BindAsync(new(tenant, plugin, "1", "test", Empty), Profile with
        {
            WorkClass = workClass
        },
            callbacks ?? new Holds(), ["hold"]);

    internal static async Task RunAsync()
    {
        await SerializationAsync();
        await FairBorrowingAsync();
        await HeavyAndQueueAsync();
        await ReplacementAndDeadlineAsync();
        await CancellationAndShutdownAsync();
        await DynamicReserveAsync();
        await PristineAdmissionChecks.RunAsync(Root, Check);
        await PayloadAndAuthorityAsync();
        await MemoryAndIdleAsync();
        await MemoryLedGrowthAsync();
        await DormantRegistryAsync();
        await CleanupFailureAsync();
        await DiagnosticsChecks.RunAsync(Root);
        await OperationLeaseChecks.RunAsync(Root);
        Check(!Directory.Exists(Root) || !Directory.EnumerateFileSystemEntries(Root).Any(), "all worker workspaces removed");
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root);
        }

        Console.WriteLine($"PASS {_checks} public scheduler assertions");
    }

    private static void Check(bool value, string name)
    {
        if (!value)
        {
            throw new Exception(name);
        }
        _checks++;
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T) { _checks++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

}
