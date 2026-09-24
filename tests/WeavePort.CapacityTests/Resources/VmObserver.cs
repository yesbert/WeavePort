using System.Diagnostics;
using System.Globalization;

namespace WeavePort.CapacityTests;

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
        for (int n = 0; n < 100 && _latest is null && !_reader.IsCompleted; n++)
        {
            await Task.Delay(100);
        }

        if (_latest is null)
        {
            throw new IOException("VM observer failed to initialize");
        }
    }

    private async Task ReadAsync()
    {
        var parser = new VmSampleParser(clock);
        while (await _process!.StandardOutput.ReadLineAsync() is { } line)
        {
            _latest = parser.Read(line) ?? _latest;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            await Commands.RunAsync("docker", "rm", "--force", _name);
        }
        finally
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
            }

            await _process.WaitForExitAsync();
            if (_reader is not null)
            {
                await _reader;
            }

            if (_errors is not null)
            {
                await _errors;
            }

            _process.Dispose();
        }
    }
}
