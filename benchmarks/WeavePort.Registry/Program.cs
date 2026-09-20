using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

if (args.Contains("--worker"))
{
    Console.WriteLine("{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}");
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var document = JsonDocument.Parse(line);
        var frame = document.RootElement;
        Console.WriteLine(JsonSerializer.Serialize(new { type = "result", id = frame.GetProperty("id").GetString(),
            value = new { tenant = frame.GetProperty("context").GetProperty("tenant").GetString() } }));
    }
    return;
}
await RegistryRun.RunAsync(int.Parse(args[0]), args[1]);

internal static class RegistryRun
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });

    internal static async Task RunAsync(int count, string output)
    {
        if (count < 4 || count > 100000) throw new ArgumentOutOfRangeException(nameof(count));
        Directory.CreateDirectory(output);
        string workspace = Path.Combine(output, "workers");
        var profile = new ProcessProfile(Environment.ProcessPath!, [typeof(RegistryRun).Assembly.Location, "--worker"],
            trustedCode: true, workspaceRoot: workspace, reservedMemoryMiB: 64);
        await using var host = new ScheduledPluginHost(new SchedulingOptions { MaximumWorkers = 4, MaximumHeavyCalls = 0,
            MaximumPristineWorkers = 0, MaximumRegistrations = count, MemoryBudgetMiB = 256 });
        var active = new ScheduledPlugin[4];
        long initialMemory = GC.GetTotalMemory(true), initialRss = Process.GetCurrentProcess().WorkingSet64;
        long began = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(Enumerable.Range(0, count), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, _) =>
        {
            var plugin = await host.RegisterAsync(new("customer-" + i, "fixture", "1", "registry", Empty), profile, new Callbacks(), []);
            if (i < 4) active[i] = plugin;
        });
        double registrationSeconds = Stopwatch.GetElapsedTime(began).TotalSeconds;
        long managedAfter = GC.GetTotalMemory(true), rssAfter = Process.GetCurrentProcess().WorkingSet64;
        foreach (var plugin in active) await plugin.InvokeAsync("echo", Empty);
        var counts = new long[4];
        began = Stopwatch.GetTimestamp();
        await Task.WhenAll(active.Select((plugin, i) => Task.Run(async () =>
        {
            while (Stopwatch.GetElapsedTime(began).TotalSeconds < 15)
            {
                var result = await plugin.InvokeAsync("echo", Empty);
                if (result.Status != "ok" || result.Value.GetProperty("tenant").GetString() != "customer-" + i)
                    throw new InvalidOperationException("Incorrect customer result");
                counts[i]++;
            }
        })));
        double elapsed = Stopwatch.GetElapsedTime(began).TotalSeconds;
        long disposal = Stopwatch.GetTimestamp();
        await host.DisposeAsync();
        var report = new { registrations = count, activeCustomers = 4, registrationSeconds, requestsPerSecond = counts.Sum() / elapsed,
            elapsed, counts, managedRegistrationDeltaMiB = (managedAfter - initialMemory) / 1048576.0,
            rssBeforeMiB = initialRss / 1048576.0, rssAfterRegistrationMiB = rssAfter / 1048576.0,
            disposalSeconds = Stopwatch.GetElapsedTime(disposal).TotalSeconds, cleanup = host.Snapshot,
            hostingSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ScheduledPluginHost).Assembly.Location))),
            scope = "Diagnostic: four hot customers plus dormant registrations; NOT many-customer throughput." };
        await File.WriteAllTextAsync(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        if (host.Snapshot.Runtime is not { Workers: 0, Bindings: 0, Tenants: 0 } || host.Snapshot.Registrations != 0)
            throw new InvalidOperationException("Incomplete registry cleanup");
    }

    private sealed class Callbacks : IHostCallbacks
    {
        public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
