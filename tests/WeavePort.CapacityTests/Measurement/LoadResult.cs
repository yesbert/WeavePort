using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Testing;

namespace WeavePort.CapacityTests;

internal sealed record LoadResult(int Tenants, string Workload, double DurationSeconds, long Requests, long Errors,
    double RequestsPerSecond, double P50Ms, double P95Ms, double P99Ms, double MaxMs, int MinTenantRequests, int MaxTenantRequests, TenantRequests[] Raw, double OfferedSeconds, long WindowCompletions, double WindowRequestsPerSecond, double DrainSeconds, double GcPauseMs, long AllocatedBytes,
    double? WorstTenantP99Ms = null, int UnservedTenants = 0, bool GcCountersExcludeAnalysis = false)
{
    internal bool ExceedsQualityBoundary => Errors > Requests * 0.01 || UnservedTenants > 0 ||
        P99Ms > (Workload == "payload" ? 2000 : 500) || WorstTenantP99Ms > (Workload == "payload" ? 2000 : 500);
}
