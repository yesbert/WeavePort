using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeavePort.LoadTests;

internal sealed class ResourceSampler : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _reader;
    private readonly Task<string> _errors;
    private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _stopped;
    internal List<object> Samples { get; } = [];

    internal ResourceSampler(string instance, TimeProvider clock)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in new[] { "stats", "--no-trunc", "--format", "{{json .}}", instance })
        {
            info.ArgumentList.Add(argument);
        }

        _process = Process.Start(info) ?? throw new IOException("Could not launch resource sampler.");
        _errors = _process.StandardError.ReadToEndAsync();
        _reader = ReadAsync(clock);
    }

    internal Task WaitForFirstSampleAsync() => _first.Task.WaitAsync(TimeSpan.FromSeconds(10));

    private async Task ReadAsync(TimeProvider clock)
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                string json = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                using JsonDocument sample = JsonDocument.Parse(json);
                Samples.Add(new
                {
                    observedUtc = clock.GetUtcNow(),
                    statistics = sample.RootElement.Clone()
                });
                _first.TrySetResult();
            }
            if (Samples.Count == 0)
            {
                _first.TrySetException(new IOException("Docker emitted no resource samples: " + await _errors));
            }
        }
        catch (Exception error)
        {
            _first.TrySetException(error);
            throw;
        }
    }

    internal async Task StopAsync()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        if (!_process.HasExited)
        {
            _process.Kill(true);
        }

        await _process.WaitForExitAsync();
        await _reader;
        await _errors;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _process.Dispose();
    }
}
