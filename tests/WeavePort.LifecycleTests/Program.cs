using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Runner;
using WeavePort.Testing;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
string root = Path.GetFullPath(args[0]);
string output = Path.Combine(root, "reports/lifecycle", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-ffffff"));
Directory.CreateDirectory(output);
await Provenance.WriteAsync(root, output, "lifecycle", TimeProvider.System);
var clock = new OffsetClock();
await using var host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 132, MemoryBudgetMiB: 33792,
    MaximumPristineWorkers: 4, MaintenanceInterval: TimeSpan.FromHours(1)), timeProvider: clock);
var profile = new DockerProfile("weaveport-poc-python:1", Timeout: TimeSpan.FromSeconds(10), IdleTimeout: TimeSpan.FromMinutes(1));
var sessions = new IPluginSession?[128];
var phases = new List<object>();
var lines = new List<string>();
var errors = new List<string>();
string[] prior = (await Docker("ps", "-aq", "--filter", "label=weaveport.poc=true")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
try
{
    long start = Stopwatch.GetTimestamp();
    await host.PrewarmAsync(profile, "1", 4);
    await Sample("pristine-4", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    start = Stopwatch.GetTimestamp();
    await Parallel.ForEachAsync(Enumerable.Range(0, sessions.Length), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, token) =>
    {
        sessions[i] = await host.BindAsync(new PluginContext("lifecycle-" + i, "demo", "1", "default",
            JsonSerializer.SerializeToElement(new { secret = "synthetic-" + i })), profile, new NoCallbacks(), [], token);
        if (ContractChecks.Successful(await sessions[i]!.InvokeAsync("counter", JsonSerializer.SerializeToElement(new { }), token)).GetInt32() != 1)
            throw new Exception("Initial worker state was not fresh");
    });
    await Sample("128-used", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    if (host.Snapshot.Workers != 128) throw new Exception("Unexpected retained workers before expiry");
    string[] previous = sessions.Select(s => s!.Instance).ToArray();
    clock.Advance(TimeSpan.FromMinutes(2));
    start = Stopwatch.GetTimestamp();
    await host.MaintainAsync();
    await Sample("128-idle-bindings-4-pristine", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    if (host.Snapshot is not { Workers: 4, Pristine: 4, Bindings: 128 }) throw new Exception("Idle retention did not collapse to the shared reserve");
    start = Stopwatch.GetTimestamp();
    await Parallel.ForEachAsync(Enumerable.Range(0, sessions.Length), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, token) =>
    {
        IPluginSession session = sessions[i]!;
        if (ContractChecks.Successful(await session.InvokeAsync("counter", JsonSerializer.SerializeToElement(new { }), token)).GetInt32() != 1 || session.Instance == previous[i])
            throw new Exception("Idle worker was reused instead of recreated");
        JsonElement context = ContractChecks.Successful(await session.InvokeAsync("context", JsonSerializer.SerializeToElement(new { }), token));
        if (context.GetProperty("tenant").GetString() != "lifecycle-" + i || context.GetProperty("configuration").GetProperty("secret").GetString() != "synthetic-" + i)
            throw new Exception("Reactivation changed customer authority");
    });
    await Sample("128-reactivated", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    start = Stopwatch.GetTimestamp();
    await Parallel.ForEachAsync(sessions, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (session, _) => await session!.DisposeAsync());
    await host.PrewarmAsync(profile, "1", 0);
    await Sample("released", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    if (host.Snapshot is not { Workers: 0, Bindings: 0, Tenants: 0, ReservedMemoryMiB: 0 }) throw new Exception("Lifecycle leaked registrations or reservations");
}
catch (Exception error) { errors.Add(error.GetType().Name + ": " + error.Message); Environment.ExitCode = 1; }
finally
{
    await host.DisposeAsync();
    await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { tenantCount = 128, language = "python", note = "Idle age advanced through injected TimeProvider; cleanup and startup use real elapsed time. Docker memory is a rounded snapshot, not RSS. Child RSS includes shared pages and the ps probe; it is not unique physical memory and must not be added to VM/container totals. No traffic is active during resource snapshots.", phases, errors }, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(Path.Combine(output, "report.md"), "# Shared lifecycle resource experiment\n\n128 Python customers; shared reserve of four; opt-in idle release. Injected elapsed clock advances idle age; measured transitions use a real monotonic clock.\n\n| Phase | Workers | Bindings | Docker memory MiB | Host RSS MiB | Child RSS MiB | Transition ms |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: |\n" + string.Join("\n", lines) + "\n\nErrors: " + errors.Count + ". " + string.Join("; ", errors) + "\n");
    await File.WriteAllTextAsync(Path.Combine(output, "exit-code.txt"), Environment.ExitCode.ToString());
}
Console.WriteLine("Evidence directory: " + Path.GetRelativePath(root, output));

async Task Sample(string phase, double elapsedMs)
{
    string[] names = (await Docker("ps", "-aq", "--filter", "label=weaveport.poc=true")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Except(prior).ToArray();
    string raw = names.Length == 0 ? "" : await Docker(["stats", "--no-stream", "--format", "{{json .}}", .. names]);
    double memory = 0;
    foreach (string row in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        JsonElement item = JsonElement.Parse(row);
        string value = item.GetProperty("MemUsage").GetString()!.Split('/')[0].Trim();
        bool parsed = false;
        foreach ((string unit, double factor) in new[] { ("GiB", 1024d), ("MiB", 1d), ("KiB", 1d / 1024), ("B", 1d / 1048576) })
            if (value.EndsWith(unit, StringComparison.Ordinal)) { memory += double.Parse(value[..^unit.Length]) * factor; parsed = true; break; }
        if (!parsed) throw new IOException("Unusable Docker memory sample");
    }
    WorkerPoolSnapshot snapshot = host.Snapshot;
    using Process self = Process.GetCurrentProcess();
    double hostMiB = self.WorkingSet64 / 1048576d;
    using Process ps = Process.Start(new ProcessStartInfo("ps") { ArgumentList = { "-axo", "ppid=,rss=" }, RedirectStandardOutput = true })!;
    string rows = await ps.StandardOutput.ReadToEndAsync(); await ps.WaitForExitAsync();
    if (ps.ExitCode != 0) throw new IOException("Child RSS sampling failed");
    long childRssKiB = 0;
    int childCount = 0;
    foreach (string row in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] fields = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 2 && int.Parse(fields[0]) == Environment.ProcessId) { childRssKiB += long.Parse(fields[1]); childCount++; }
    }
    double childRssMiB = childRssKiB / 1024d;
    phases.Add(new { phase, elapsedMs, snapshot, dockerMemoryMiB = memory, hostRssMiB = hostMiB, childRssMiB, childCount, managedBytes = GC.GetTotalMemory(false), containers = names, rawStats = raw });
    lines.Add($"| {phase} | {snapshot.Workers} | {snapshot.Bindings} | {memory:F2} | {hostMiB:F2} | {childRssMiB:F2} | {elapsedMs:F2} |");
    Console.WriteLine($"{phase}: workers={snapshot.Workers} bindings={snapshot.Bindings} dockerMiB={memory:F2} transitionMs={elapsedMs:F2}");
}
static async Task<string> Docker(params string[] arguments)
{
    var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments) info.ArgumentList.Add(argument);
    using Process process = Process.Start(info)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync(); Task<string> error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new IOException(await error); return (await output).Trim();
}
sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
}
sealed class OffsetClock : TimeProvider
{
    private long _offset;
    public override long GetTimestamp() => TimeProvider.System.GetTimestamp() + Interlocked.Read(ref _offset);
    internal void Advance(TimeSpan time) => Interlocked.Add(ref _offset, (long)(time.TotalSeconds * TimestampFrequency));
}
