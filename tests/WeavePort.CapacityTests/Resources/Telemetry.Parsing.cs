using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeavePort.CapacityTests;

internal sealed partial class Telemetry
{
    internal static WorkerSample? ParseSample(string line, TimeProvider clock)
    {
        string json = Regex.Replace(line, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement item = document.RootElement;
        string name = item.GetProperty("Name").GetString()!;
        if (name == "--" || new[] { "MemUsage", "CPUPerc", "PIDs" }.Any(key => item.GetProperty(key).GetString()!.Contains("--", StringComparison.Ordinal)))
        {
            return null;
        }

        return new WorkerSample(clock.GetUtcNow(), name, ParseBytes(item.GetProperty("MemUsage").GetString()!.Split('/')[0].Trim()),
                    double.Parse(item.GetProperty("CPUPerc").GetString()!.TrimEnd('%'), CultureInfo.InvariantCulture),
                    int.Parse(item.GetProperty("PIDs").GetString()!, CultureInfo.InvariantCulture));
    }

    internal static double ParseBytes(string text)
    {
        foreach ((string suffix, double factor) in new[] { ("GiB", Math.Pow(1024, 3)), ("MiB", Math.Pow(1024, 2)), ("KiB", 1024d), ("GB", 1e9), ("MB", 1e6), ("kB", 1e3), ("B", 1d) })
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                return double.Parse(text[..^suffix.Length], CultureInfo.InvariantCulture) * factor;
            }
        }

        throw new FormatException("Unknown memory unit: " + text);
    }

}
