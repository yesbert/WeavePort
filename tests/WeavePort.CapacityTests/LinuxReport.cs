using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal static class LinuxReport
{
    internal static async Task WriteAsync(string directory)
    {
        var text = new StringBuilder("# Local capacity observations\n\n");
        text.AppendLine("Source: configuration.json and retained request/resource JSON (optionally lossless gzip). One outstanding request per tenant; throughput includes drain. Worst-customer p99 is recomputed from raw requests, including historical files.\n");
        foreach (string name in new[] { "stop-reason.txt", "exit-code.txt", "outer-exit-code.txt" })
            if (File.Exists(Path.Combine(directory, name))) text.AppendLine($"- {name}: {File.ReadAllText(Path.Combine(directory, name)).Trim()}");
        text.AppendLine("\n| Tenants | Workload | Duration s | Requests/s | Aggregate p99 ms | Worst customer p99 ms | Errors | Min/max calls per customer | Qualification | GC pause ms | GC counter scope |\n| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | --- |");
        string[] files = Directory.EnumerateFiles(directory).Where(p =>
            new[] { "-echo.json", "-payload.json", "-delay.json", "recovery.json" }.Any(s => p.EndsWith(s, StringComparison.Ordinal) || p.EndsWith(s + ".gz", StringComparison.Ordinal)))
            .OrderBy(p => int.TryParse(Path.GetFileName(p).Split('-')[0], out int n) ? n : int.MaxValue).ThenBy(p => p, StringComparer.Ordinal).ToArray();
        foreach (string file in files)
        {
            using JsonDocument document = Read(file);
            JsonElement row = document.RootElement;
            JsonElement[] tenants = row.GetProperty("Raw").EnumerateArray().ToArray();
            long requests = tenants.Sum(t => (long)t.GetProperty("LatenciesMs").GetArrayLength());
            long errors = tenants.Sum(t => t.GetProperty("StatusCounts").EnumerateObject().Where(s => s.Name != "ok").Sum(s => s.Value.GetInt64()));
            if (requests != row.GetProperty("Requests").GetInt64() || errors != row.GetProperty("Errors").GetInt64()) throw new InvalidDataException("Raw outcomes disagree with aggregate counts");
            int unserved = tenants.Count(t => t.GetProperty("LatenciesMs").GetArrayLength() == 0);
            double worst = tenants.Where(t => t.GetProperty("LatenciesMs").GetArrayLength() > 0)
                .Max(t => ContractChecks.Percentile(t.GetProperty("LatenciesMs").EnumerateArray().Select(v => v.GetDouble()), 99));
            if (row.TryGetProperty("WorstTenantP99Ms", out var stored) && stored.ValueKind == JsonValueKind.Number && Math.Abs(stored.GetDouble() - worst) > 1e-8)
                throw new InvalidDataException("Worst-customer percentile mismatch");
            double D(string key) => row.GetProperty(key).GetDouble();
            double limit = row.GetProperty("Workload").GetString() == "payload" ? 2000 : 500;
            string qualified = D("DurationSeconds") + 0.01 < D("OfferedSeconds") ? "shortened" :
                unserved > 0 ? "unserved customer" : worst > limit || D("P99Ms") > limit || errors > requests * 0.01 ? "quality boundary" : "within current stage limits";
            string scope = row.TryGetProperty("GcCountersExcludeAnalysis", out var scopeFlag) && scopeFlag.GetBoolean() ? "load + drain" : "includes analysis";
            string workload = Path.GetFileName(file).StartsWith("recovery", StringComparison.Ordinal) ? "recovery" : row.GetProperty("Workload").GetString()!;
            text.AppendLine($"| {D("Tenants")} | {workload} | {D("DurationSeconds"):F3} | {D("RequestsPerSecond"):F1} | {D("P99Ms"):F2} | {worst:F2} | {errors} | {D("MinTenantRequests")}/{D("MaxTenantRequests")} | {qualified} | {D("GcPauseMs"):F2} | {scope} |");
        }
        text.AppendLine("\nQualification applies the current per-customer/aggregate thresholds (500 ms small/delay, 2000 ms payload, at most 1% errors, no unserved customers). It does not override a failed outer run, stale telemetry or failed recovery. Historical outer exits used their original guard policy. These thresholds are experiment bounds, not production SLOs.\n");
        string resourceFile = Path.Combine(directory, "resources.json");
        if (!File.Exists(resourceFile)) resourceFile += ".gz";
        if (File.Exists(resourceFile))
        {
            using JsonDocument document = Read(resourceFile);
            text.AppendLine("## Sampled resource maxima\n\n| Phase | Coordinator cgroup MiB | Host RSS MiB | Worker Docker memory MiB | Minimum VM available GiB |\n| --- | ---: | ---: | ---: | ---: |");
            foreach (var group in document.RootElement.EnumerateArray().Where(r => !r.GetProperty("Phase").GetString()!.EndsWith(":startup", StringComparison.Ordinal) && r.GetProperty("Workers").GetArrayLength() > 0)
                         .GroupBy(r => r.GetProperty("Phase").GetString()))
            {
                double? cgroup = group.Where(r => r.TryGetProperty("CoordinatorCgroupMemoryBytes", out var v) && v.ValueKind == JsonValueKind.Number)
                    .Select(r => (double?)r.GetProperty("CoordinatorCgroupMemoryBytes").GetDouble()).Max();
                double host = group.Max(r => r.GetProperty("HostRssBytes").GetDouble()) / 1048576;
                double workers = group.Max(r => r.GetProperty("Workers").EnumerateArray().Sum(w => w.GetProperty("MemoryBytes").GetDouble())) / 1048576;
                double available = group.Min(r => r.GetProperty("Vm").GetProperty("AvailableBytes").GetDouble()) / 1073741824;
                text.AppendLine($"| {group.Key} | {(cgroup is { } bytes ? (bytes / 1048576).ToString("F1") : "unavailable")} | {host:F1} | {workers:F1} | {available:F2} |");
            }
            text.AppendLine("\nIndependent sampled maxima are not exact simultaneous peaks. Phase samples can include report writing and drain; worker samples retain their own timestamps. VM scope includes unrelated workloads. Process RSS must not be summed with cgroup/container memory as if disjoint.");
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "report.md"), text.ToString());
        Console.WriteLine("Reported " + Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)));
    }

    private static JsonDocument Read(string path)
    {
        using Stream file = File.OpenRead(path);
        if (!path.EndsWith(".gz", StringComparison.Ordinal)) return JsonDocument.Parse(file);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
