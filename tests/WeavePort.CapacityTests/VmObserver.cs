using System.Diagnostics;
using System.Globalization;

namespace WeavePort.CapacityTests;

internal sealed record VmSample(DateTimeOffset Utc, long TotalBytes, long AvailableBytes, double CpuPercent, string MemoryPressure);

internal sealed class VmObserver(TimeProvider clock) : IAsyncDisposable
{
    private readonly string _name = "weaveport-capacity-observer-" + Guid.NewGuid().ToString("N");
    private Process? _process;
    private Task? _reader;
    private Task<string>? _errors;
    private VmSample? _latest;
    internal VmSample Latest => _latest ?? throw new IOException("VM telemetry unavailable");

    internal async Task StartAsync()
    {
        _process = Commands.Start("docker", "run", "--rm", "--name", _name, "--network", "none", "--read-only", "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges", "--user", "65532:65532", "--memory", "64m", "--cpus", "0.1", "--pids-limit", "16",
            "--entrypoint", "/bin/sh", "weaveport-poc-csharp:1", "-c",
            "while true; do echo BEGIN; cat /proc/meminfo; head -n 1 /proc/stat; cat /proc/pressure/memory; echo END; sleep 1; done");
        _errors = _process.StandardError.ReadToEndAsync();
        _reader = ReadAsync();
        for (int n = 0; n < 100 && _latest is null && !_reader.IsCompleted; n++) await Task.Delay(100);
        if (_latest is null) throw new IOException("VM observer failed to initialize");
    }

    private async Task ReadAsync()
    {
        long total = 0, available = 0, previousTicks = 0, previousIdle = 0;
        double cpu = 0;
        string pressure = "";
        while (await _process!.StandardOutput.ReadLineAsync() is { } line)
        {
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith("MemTotal:")) total = long.Parse(words[1], CultureInfo.InvariantCulture) * 1024;
            if (line.StartsWith("MemAvailable:")) available = long.Parse(words[1], CultureInfo.InvariantCulture) * 1024;
            if (line.StartsWith("some ")) pressure = line;
            if (line.StartsWith("cpu "))
            {
                long[] values = words.Skip(1).Take(8).Select(x => long.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                long ticks = values.Sum(), idle = values[3] + values[4];
                cpu = previousTicks == 0 || ticks == previousTicks ? 0 : 100d * (1 - (double)(idle - previousIdle) / (ticks - previousTicks));
                previousTicks = ticks;
                previousIdle = idle;
            }
            if (line == "END") _latest = new VmSample(clock.GetUtcNow(), total, available, cpu, pressure);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null) return;
        try { await Commands.RunAsync("docker", "rm", "--force", _name); }
        finally
        {
            if (!_process.HasExited) _process.Kill(true);
            await _process.WaitForExitAsync();
            if (_reader is not null) await _reader;
            if (_errors is not null) await _errors;
            _process.Dispose();
        }
    }
}
