using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeavePort.CapacityTests;

internal sealed record WorkerSample(DateTimeOffset Utc, string Name, double MemoryBytes, double CpuPercent, int Pids);
