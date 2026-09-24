using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal static class DiagnosticReport
{
    internal static async Task WriteAsync(string directory)
    {
        var report = new StringBuilder("# Payload diagnostic observations\n\n");
        report.AppendLine("Closed-loop local experiment, one outstanding call per tenant. Window throughput counts completions within the offered interval; total throughput includes final drain. Percentiles include failed invocations. GC pause is cumulative stop-the-world time, not per-call CPU time. Files with GcCountersExcludeAnalysis=true capture load and drain only; older files also include the following percentile calculation.\n");
        report.AppendLine("| Tenants | Requests/s including drain | Requests/s in window | p99 ms | Errors | Drain s | GC pause ms |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        string[] files = Directory.EnumerateFiles(directory).Where(p => p.EndsWith("-payload.json", StringComparison.Ordinal) || p.EndsWith("-payload.json.gz", StringComparison.Ordinal)).OrderBy(p => int.Parse(Path.GetFileName(p).Split('-')[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        foreach (string source in files)
        {
            AppendRequestRow(source, report);
        }
        report.AppendLine("\n## Transport phase observations\n\nSuccessful calls only. Write includes serialization; read includes waiting for the first byte and parsing. These overlapping phase statistics must not be added as independent costs. Failed reads can have no completion tag; zero is not evidence of a fast failed read.\n");
        report.AppendLine("| Tenants | Mean write ms | Mean serialize ms | Mean read ms | Mean first-byte ms | Mean parse ms |\n| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (string source in files)
        {
            AppendTransportRow(source, report);
        }
        report.AppendLine("\n## Worker timing split\n\nSynthetic diagnostic fixture only. Receive occurs after the complete input line, before JSON parsing; sending occurs before response serialization. Host/VM wall-clock offset is estimated separately for each tenant from its minimum-RTT calibration call. Clock changes after calibration and asymmetric paths limit accuracy; use these approximate splits to locate second-scale delays, not submillisecond costs. Only successful calls are included.\n");
        report.AppendLine("| Tenants | Mean inbound ms | p99 inbound ms | Mean outbound ms | p99 outbound ms | Mean worker parse ms | Max calibration half-width ms |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (string source in files)
        {
            AppendWorkerRow(directory, source, report);
        }
        report.AppendLine("\nInspect configuration.json, raw per-stage JSON (or lossless .json.gz), resources.json and exit-code.txt alongside these statistics. Preliminary runs may also have a process-exit-code.txt and limitation.md that override an inner success code. This report is descriptive, not an SLO or proof of a particular internal Docker defect.");
        await File.WriteAllTextAsync(Path.Combine(directory, "diagnostic-report.md"), report.ToString());
    }

    private static void AppendRequestRow(string path, StringBuilder report)
    {

        using JsonDocument document = Read(path);
        JsonElement data = document.RootElement;
        double D(string key) => data.GetProperty(key).GetDouble();
        report.AppendLine($"| {D("Tenants")} | {D("RequestsPerSecond"):F1} | {D("WindowRequestsPerSecond"):F1} | {D("P99Ms"):F2} | {D("Errors")} | {D("DrainSeconds"):F3} | {D("GcPauseMs"):F2} |");
    }
    private static void AppendTransportRow(string path, StringBuilder report)
    {

        using JsonDocument document = Read(path);
        JsonElement data = document.RootElement;
        JsonElement[] observations = data.GetProperty("Raw").EnumerateArray()
            .Where(r => r.TryGetProperty("Observations", out var o) && o.ValueKind == JsonValueKind.Array)
            .SelectMany(r => r.GetProperty("Observations").EnumerateArray()).Where(o => o.GetProperty("Status").GetString() == "ok").ToArray();
        long observed = data.GetProperty("Raw").EnumerateArray()
            .Where(r => r.TryGetProperty("Observations", out var o) && o.ValueKind == JsonValueKind.Array)
            .Sum(r => (long)r.GetProperty("Observations").GetArrayLength());
        ValidateTimeline(data, observed);
        if (observations.Length == 0)
        {
            return;
        }

        double Mean(string key) => observations.Average(o => o.GetProperty(key).GetDouble());
        report.AppendLine($"| {data.GetProperty("Tenants")} | {Mean("WriteMs"):F3} | {Mean("SerializeMs"):F3} | {Mean("ReadMs"):F3} | {Mean("FirstByteMs"):F3} | {Mean("ParseMs"):F3} |");
    }
    private static void AppendWorkerRow(string directory, string path, StringBuilder report)
    {

        using JsonDocument document = Read(path);
        JsonElement data = document.RootElement;
        string calibration = Path.Combine(directory, data.GetProperty("Tenants") + "-calibration.json");
        if (!File.Exists(calibration) && File.Exists(calibration + ".gz"))
        {
            calibration += ".gz";
        }

        if (!File.Exists(calibration))
        {
            return;
        }

        using JsonDocument clocks = Read(calibration);
        var estimates = clocks.RootElement.EnumerateArray().GroupBy(c => c.GetProperty("Index").GetInt32()).ToDictionary(g => g.Key, g =>
        {
            var choices = g.Select(c =>
            {
                JsonElement t = c.GetProperty("timing");
                double lower = t.GetProperty("sendingUtcMs").GetDouble() - c.GetProperty("hostEndUtcMs").GetDouble();
                double upper = t.GetProperty("receivedUtcMs").GetDouble() - c.GetProperty("hostStartUtcMs").GetDouble();
                return (Offset: (lower + upper) / 2, HalfWidth: (upper - lower) / 2);
            });
            return choices.MinBy(c => c.HalfWidth);
        });
        var inbound = new List<double>();
        var outbound = new List<double>();
        var parse = new List<double>();
        foreach (JsonElement tenant in data.GetProperty("Raw").EnumerateArray())
        {
            AddTenantTimings(tenant, estimates[tenant.GetProperty("Tenant").GetInt32()].Offset, inbound, outbound, parse);
        }
        if (inbound.Count > 0)
        {
            report.AppendLine($"| {data.GetProperty("Tenants")} | {inbound.Average():F3} | {ContractChecks.Percentile(inbound, 99):F3} | {outbound.Average():F3} | {ContractChecks.Percentile(outbound, 99):F3} | {parse.Average():F3} | {estimates.Values.Max(c => c.HalfWidth):F3} |");
        }
    }
    private static void ValidateTimeline(JsonElement data, long observed)
    {
        if (observed == 0)
        {
            return;
        }

        var all = data.GetProperty("Raw").EnumerateArray().SelectMany(r => r.GetProperty("Observations").EnumerateArray()).ToArray();
        long window = all.LongCount(o => o.GetProperty("CompletedMs").GetDouble() <= data.GetProperty("OfferedSeconds").GetDouble() * 1000);
        if (observed != data.GetProperty("Requests").GetInt64() || window != data.GetProperty("WindowCompletions").GetInt64() ||
            all.Any(o => o.GetProperty("CompletedMs").GetDouble() < o.GetProperty("StartedMs").GetDouble()))
        {
            throw new InvalidDataException("Request timeline does not match aggregate counters");
        }
    }
    private static void AddTenantTimings(JsonElement tenant, double offset, List<double> inbound, List<double> outbound, List<double> parse)
    {
        foreach (JsonElement observation in tenant.GetProperty("Observations").EnumerateArray())
        {

            if (observation.GetProperty("Status").GetString() != "ok" || observation.GetProperty("WorkerTiming").ValueKind != JsonValueKind.Object)
            {
                return;
            }

            JsonElement worker = observation.GetProperty("WorkerTiming");
            inbound.Add(worker.GetProperty("receivedUtcMs").GetDouble() - offset - observation.GetProperty("HostStartedUtcMs").GetDouble());
            outbound.Add(observation.GetProperty("HostCompletedUtcMs").GetDouble() - worker.GetProperty("sendingUtcMs").GetDouble() + offset);
            parse.Add(worker.GetProperty("parseMs").GetDouble());
        }
    }
    private static JsonDocument Read(string path)
    {
        using Stream file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(file, CompressionMode.Decompress) : file;
        return JsonDocument.Parse(input);
    }
}
