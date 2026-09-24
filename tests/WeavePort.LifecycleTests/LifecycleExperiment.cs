using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Runner;
using WeavePort.Testing;

internal sealed class LifecycleExperiment
{
    private readonly OffsetClock _clock = new();
    private readonly PluginHost _host;
    private readonly DockerProfile _profile = new("weaveport-poc-python:1", Timeout: TimeSpan.FromSeconds(10), IdleTimeout: TimeSpan.FromMinutes(1));
    private readonly IPluginSession?[] _sessions = new IPluginSession?[128];
    private readonly List<object> _phases = [];
    private readonly List<string> _lines = [];
    private readonly List<string> _errors = [];
    private string[] _prior = [];
    private readonly string _output;
    private readonly string _root;

    internal LifecycleExperiment(string root)
    {
        _root = root;
        _output = Path.Combine(root, "reports/lifecycle", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-ffffff"));
        _host = new PluginHost(options: new WorkerPoolOptions(MaximumWorkers: 132, MemoryBudgetMiB: 33792,
            MaximumPristineWorkers: 4, MaintenanceInterval: TimeSpan.FromHours(1)), timeProvider: _clock);
    }
    internal async Task RunAsync()
    {
        await using var ownedHost = _host;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(_output);
        await Provenance.WriteAsync(_root, _output, "lifecycle", TimeProvider.System);
        _prior = (await DockerAsync("ps", "-aq", "--filter", "label=weaveport.poc=true")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            await PrewarmAsync();
            await ActivateAsync();
            string[] previous = _sessions.Select(s => s!.Instance).ToArray();
            await ExpireAsync();
            await ReactivateAsync(previous);
            await ReleaseAsync();
        }
        catch (Exception error) { _errors.Add(error.GetType().Name + ": " + error.Message); Environment.ExitCode = 1; }
        finally
        {
            await _host.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(_output, "results.json"), JsonSerializer.Serialize(new
            {
                tenantCount = 128,
                language = "python",
                note = "Idle age advanced through injected TimeProvider; cleanup and startup use real elapsed time. Docker memory is a rounded snapshot, not RSS. Child RSS includes shared pages and the ps probe; it is not unique physical memory and must not be added to VM/container totals. No traffic is active during resource snapshots.",
                phases = _phases,
                errors = _errors
            }, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(Path.Combine(_output, "report.md"), "# Shared lifecycle resource experiment\n\n128 Python customers; shared reserve of four; opt-in idle release. Injected elapsed clock advances idle age; measured transitions use a real monotonic clock.\n\n| Phase | Workers | Bindings | Docker memory MiB | Host RSS MiB | Child RSS MiB | Transition ms |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: |\n" + string.Join("\n", _lines) + "\n\nErrors: " + _errors.Count + ". " + string.Join("; ", _errors) + "\n");
            await File.WriteAllTextAsync(Path.Combine(_output, "exit-code.txt"), Environment.ExitCode.ToString());
        }
        Console.WriteLine("Evidence directory: " + Path.GetRelativePath(_root, _output));

    }
    private async Task PrewarmAsync()
    {
        long start = Stopwatch.GetTimestamp();
        await _host.PrewarmAsync(_profile, "1", 4);
        await SampleAsync("pristine-4", Stopwatch.GetElapsedTime(start).TotalMilliseconds);

    }
    private async Task ActivateAsync()
    {
        long start = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(Enumerable.Range(0, _sessions.Length), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, token) =>
        {
            _sessions[i] = await _host.BindAsync(new PluginContext("lifecycle-" + i, "demo", "1", "default",
                JsonSerializer.SerializeToElement(new
                {
                    secret = "synthetic-" + i
                })), _profile, new NoCallbacks(), [], cancellationToken: token);
            if (ContractChecks.Successful(await _sessions[i]!.InvokeAsync("counter", JsonSerializer.SerializeToElement(new
            {
            }), token)).GetInt32() != 1)
            {
                throw new Exception("Initial worker state was not fresh");
            }
        });
        await SampleAsync("128-used", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (_host.Snapshot.Workers != 128)
        {
            throw new Exception("Unexpected retained workers before expiry");
        }

    }
    private async Task ExpireAsync()
    {
        _clock.Advance(TimeSpan.FromMinutes(2));
        long start = Stopwatch.GetTimestamp();
        await _host.MaintainAsync();
        await SampleAsync("128-idle-bindings-4-pristine", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (_host.Snapshot is not { Workers: 4, Pristine: 4, Bindings: 128 })
        {
            throw new Exception("Idle retention did not collapse to the shared reserve");
        }

    }
    private async Task ReactivateAsync(string[] previous)
    {
        long start = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(Enumerable.Range(0, _sessions.Length), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, token) =>
        {
            IPluginSession session = _sessions[i]!;
            if (ContractChecks.Successful(await session.InvokeAsync("counter", JsonSerializer.SerializeToElement(new
            {
            }), token)).GetInt32() != 1 || session.Instance == previous[i])
            {
                throw new Exception("Idle worker was reused instead of recreated");
            }

            JsonElement context = ContractChecks.Successful(await session.InvokeAsync("context", JsonSerializer.SerializeToElement(new
            {
            }), token));
            if (context.GetProperty("tenant").GetString() != "lifecycle-" + i || context.GetProperty("configuration").GetProperty("secret").GetString() != "synthetic-" + i)
            {
                throw new Exception("Reactivation changed customer authority");
            }
        });
        await SampleAsync("128-reactivated", Stopwatch.GetElapsedTime(start).TotalMilliseconds);

    }
    private async Task ReleaseAsync()
    {
        long start = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(_sessions, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (session, _) => await session!.DisposeAsync());
        await _host.PrewarmAsync(_profile, "1", 0);
        await SampleAsync("released", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (_host.Snapshot is not { Workers: 0, Bindings: 0, Tenants: 0, ReservedMemoryMiB: 0 })
        {
            throw new Exception("Lifecycle leaked registrations or reservations");
        }
    }
    private async Task SampleAsync(string phase, double elapsedMs)
    {
        string[] names = (await DockerAsync("ps", "-aq", "--filter", "label=weaveport.poc=true")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Except(_prior).ToArray();
        string raw = names.Length == 0 ? "" : await DockerAsync(["stats", "--no-stream", "--format", "{{json .}}", .. names]);
        double memory = 0;
        foreach (string row in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            memory += ParseMemoryMiB(row);
        }
        WorkerPoolSnapshot snapshot = _host.Snapshot;
        using Process self = Process.GetCurrentProcess();
        double hostMiB = self.WorkingSet64 / 1048576d;
        using Process ps = Process.Start(new ProcessStartInfo("ps") { ArgumentList = { "-axo", "ppid=,rss=" }, RedirectStandardOutput = true })!;
        string rows = await ps.StandardOutput.ReadToEndAsync();
        await ps.WaitForExitAsync();
        if (ps.ExitCode != 0)
        {
            throw new IOException("Child RSS sampling failed");
        }

        long childRssKiB = 0;
        int childCount = 0;
        foreach (string row in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || int.Parse(fields[0]) != Environment.ProcessId)
            {
                continue;
            }
            childRssKiB += long.Parse(fields[1]);
            childCount++;
        }
        double childRssMiB = childRssKiB / 1024d;
        _phases.Add(new
        {
            phase,
            elapsedMs,
            snapshot,
            dockerMemoryMiB = memory,
            hostRssMiB = hostMiB,
            childRssMiB,
            childCount,
            managedBytes = GC.GetTotalMemory(false),
            containers = names,
            rawStats = raw
        });
        _lines.Add($"| {phase} | {snapshot.Workers} | {snapshot.Bindings} | {memory:F2} | {hostMiB:F2} | {childRssMiB:F2} | {elapsedMs:F2} |");
        Console.WriteLine($"{phase}: workers={snapshot.Workers} bindings={snapshot.Bindings} dockerMiB={memory:F2} transitionMs={elapsedMs:F2}");
    }
    private static double ParseMemoryMiB(string row)
    {
        JsonElement item = JsonElement.Parse(row);
        string value = item.GetProperty("MemUsage").GetString()!.Split('/')[0].Trim();
        foreach ((string unit, double factor) in new[] { ("GiB", 1024d), ("MiB", 1d), ("KiB", 1d / 1024), ("B", 1d / 1048576) })
        {
            if (!value.EndsWith(unit, StringComparison.Ordinal))
            {
                continue;
            }
            return double.Parse(value[..^unit.Length], CultureInfo.InvariantCulture) * factor;
        }
        throw new IOException("Unusable Docker memory sample");
    }

    private static async Task<string> DockerAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new IOException(await error);
        }

        return (await output).Trim();
    }
}
