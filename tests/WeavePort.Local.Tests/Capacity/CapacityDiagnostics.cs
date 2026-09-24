using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using WeavePort.LocalTools;

internal sealed class CapacityDiagnostics : IDisposable
{
    private readonly ConcurrentQueue<object> _errors = new();
    private readonly bool _enabled = Environment.GetEnvironmentVariable("WEAVEPORT_LOCAL_DIAGNOSE") == "1";
    private int _count;

    internal CapacityDiagnostics()
    {
        if (_enabled)
        {
            AppDomain.CurrentDomain.FirstChanceException += OnException;
        }
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        Exception error = args.Exception;
        if (error is not (InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException))
        {
            return;
        }

        if (Interlocked.Increment(ref _count) > 100)
        {
            return;
        }
        // Synthetic-fixture diagnostic only. No payloads or exception messages are retained.
        _errors.Enqueue(new
        {
            type = error.GetType().Name,
            stack = error.StackTrace
        });
    }

    internal Task SaveAsync(string output) => File.WriteAllTextAsync(Path.Combine(output, "exceptions.json"),
        JsonSerializer.Serialize(new
        {
            enabled = _enabled,
            observed = _count,
            errors = _errors
        }, new JsonSerializerOptions { WriteIndented = true }));

    public void Dispose()
    {
        if (_enabled)
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnException;
        }
    }

    internal static async Task<double?> SwapMiBAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        string value = await LocalConfiguration.VersionAsync("/usr/sbin/sysctl", "-n", "vm.swapusage");
        string amount = value.Split("used = ", StringSplitOptions.None)[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        double number = double.Parse(amount[..^1], CultureInfo.InvariantCulture);
        return amount[^1] switch
        {
            'M' => number,
            'G' => number * 1024,
            'K' => number / 1024,
            _ => throw new InvalidDataException("Unknown swap unit")
        };
    }
}
