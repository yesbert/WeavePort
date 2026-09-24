using System.Diagnostics;
using System.Globalization;

namespace WeavePort.CapacityTests;

internal sealed record VmSample(DateTimeOffset Utc, long TotalBytes, long AvailableBytes, double CpuPercent, string MemoryPressure);
