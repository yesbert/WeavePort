using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

internal sealed partial class DensityResources(DensityConfig config, CancellationTokenSource stop, List<IPluginSession> clients, Func<WorkerPoolSnapshot> snapshot)
{
    private double _peakHost, _peakChildren, _peakCombined;
    private int _samples, _peakWorkers;
    internal string? Failure { get; private set; }
    internal object Summary() => new
    {
        peakHostMiB = _peakHost,
        peakChildRssMiB = _peakChildren,
        peakCombinedRssMiB = _peakCombined,
        samples = _samples,
        peakWorkers = _peakWorkers,
        scope = config.Adapter == "docker" ? "macOS host and owned Docker CLI RSS only; container memory recorded separately by supervisor" :
        "macOS host/load-generator and owned child process RSS; sampled shared pages may be counted twice"
    };
    internal async Task RunAsync()
    {
        await using var output = new StreamWriter(Path.Combine(config.Output, "resources.jsonl"));
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var rows = await ReadProcessesAsync();
                var owned = OwnedProcesses(rows);
                var runtime = snapshot();
                _peakWorkers = Math.Max(_peakWorkers, runtime.Workers);
                double host = rows.Single(row => row.Pid == Environment.ProcessId).Rss;
                double children = rows.Where(row => row.Pid != Environment.ProcessId && owned.Contains(row.Pid)).Sum(row => row.Rss);
                _peakHost = Math.Max(_peakHost, host);
                _peakChildren = Math.Max(_peakChildren, children);
                _peakCombined = Math.Max(_peakCombined, host + children);
                _samples++;
                string[] dockerInstances = config.Adapter == "docker" ? await ReadDockerInstancesAsync(owned) : [];
                await output.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    at = DateTimeOffset.UtcNow,
                    hostMiB = host,
                    childrenMiB = children,
                    runtime,
                    instances = clients.Select(c => c.Instance).Where(id => id.Length > 0).Concat(dockerInstances).Distinct().ToArray()
                }));
                await output.FlushAsync();
                if (host > config.MaxHostRssMiB || host + children > config.MaxOwnedRssMiB || File.Exists(Path.Combine(config.Output, "STOP")))
                {
                    Failure = "resource-or-operator-stop";
                    stop.Cancel();
                    break;
                }
                await Task.Delay(500, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { Failure = "observer-" + error.GetType().Name; stop.Cancel(); }
    }
    private static async Task<ProcessRow[]> ReadProcessesAsync()
    {
        var info = new ProcessStartInfo("/bin/ps") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("-axo");
        info.ArgumentList.Add("pid=,ppid=,rss=");
        using var ps = Process.Start(info)!;
        string lines = await ps.StandardOutput.ReadToEndAsync();
        await ps.WaitForExitAsync();
        if (ps.ExitCode != 0)
        {
            throw new IOException("RSS observer failed");
        }
        return lines.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => new ProcessRow(int.Parse(parts[0]), int.Parse(parts[1]), double.Parse(parts[2]) / 1024))
            .Where(row => row.Pid != ps.Id).ToArray();
    }

    private static HashSet<int> OwnedProcesses(ProcessRow[] rows)
    {
        var owned = new HashSet<int> { Environment.ProcessId };
        while (AddDescendants(rows, owned))
        {
            // Repeat until every generation of owned child processes has been included.
        }
        return owned;
    }

    private static bool AddDescendants(ProcessRow[] rows, HashSet<int> owned)
    {
        bool added = false;
        foreach (var row in rows)
        {
            if (!owned.Contains(row.Parent))
            {
                continue;
            }
            added |= owned.Add(row.Pid);
        }
        return added;
    }

    private sealed record ProcessRow(int Pid, int Parent, double Rss);

}
