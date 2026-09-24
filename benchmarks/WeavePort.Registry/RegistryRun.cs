using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal static class RegistryRun
{
    private const int ActiveCustomers = 4;
    private const double BytesPerMiB = 1048576;
    private const int MeasurementSeconds = 15;

    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });

    internal static async Task RunAsync(int count, string output)
    {
        if (count < 4 || count > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Directory.CreateDirectory(output);
        string workspace = Path.Combine(output, "workers");
        var profile = new ProcessProfile(Environment.ProcessPath!, [typeof(RegistryRun).Assembly.Location, "--worker"],
            trustedCode: true, workspaceRoot: workspace, reservedMemoryMiB: 64);
        await using var host = CreateHost(count);
        var active = new IPluginSession[ActiveCustomers];
        using var process = Process.GetCurrentProcess();
        long initialMemory = GC.GetTotalMemory(true), initialRss = process.WorkingSet64;
        long began = Stopwatch.GetTimestamp();
        await RegisterAsync(host, profile, count, active);
        double registrationSeconds = Stopwatch.GetElapsedTime(began).TotalSeconds;
        process.Refresh();
        long managedAfter = GC.GetTotalMemory(true), rssAfter = process.WorkingSet64;
        foreach (var plugin in active)
        {
            await plugin.InvokeAsync("echo", Empty);
        }

        var counts = new long[ActiveCustomers];
        began = Stopwatch.GetTimestamp();
        await Task.WhenAll(active.Select((plugin, lane) => Task.Run(async () =>
        {
            counts[lane] = await RunCustomerAsync(plugin, lane, began);
        })));
        double elapsed = Stopwatch.GetElapsedTime(began).TotalSeconds;
        long disposal = Stopwatch.GetTimestamp();
        await host.DisposeAsync();
        var report = new
        {
            registrations = count,
            activeCustomers = ActiveCustomers,
            registrationSeconds,
            requestsPerSecond = counts.Sum() / elapsed,
            elapsed,
            counts,
            managedRegistrationDeltaMiB = (managedAfter - initialMemory) / BytesPerMiB,
            rssBeforeMiB = initialRss / BytesPerMiB,
            rssAfterRegistrationMiB = rssAfter / BytesPerMiB,
            disposalSeconds = Stopwatch.GetElapsedTime(disposal).TotalSeconds,
            cleanup = host.Scheduling,
            hostingSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PluginHost).Assembly.Location))),
            scope = "Diagnostic: four hot customers plus dormant registrations; NOT many-customer throughput."
        };
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        if (host.Snapshot is not { Workers: 0, Bindings: 0, Tenants: 0 } || host.Scheduling!.Registrations != 0)
        {
            throw new InvalidOperationException("Incomplete registry cleanup");
        }
    }

    private static PluginHost CreateHost(int count)
    {
        return new PluginHost(new SchedulingOptions
        {
            MaximumWorkers = ActiveCustomers,
            MaximumHeavyCalls = 0,
            MaximumPristineWorkers = 0,
            MaximumRegistrations = count,
            MemoryBudgetMiB = 256
        });
    }

    private static async Task RegisterAsync(PluginHost host, ProcessProfile profile, int count, IPluginSession[] active)
    {
        await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, _) =>
        {
            var plugin = await host.BindAsync(new("customer-" + i, "fixture", "1", "registry", Empty), profile, new Callbacks(), []);
            if (i < active.Length)
            {
                active[i] = plugin;
            }
        });
    }

    private static async Task<long> RunCustomerAsync(IPluginSession plugin, int lane, long began)
    {
        long count = 0;
        while (Stopwatch.GetElapsedTime(began).TotalSeconds < MeasurementSeconds)
        {
            var result = await plugin.InvokeAsync("echo", Empty);
            if (result.Status != "ok" || result.Value.GetProperty("tenant").GetString() != "customer-" + lane)
            {
                throw new InvalidOperationException("Incorrect customer result");
            }
            count++;
        }
        return count;
    }

    private sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
