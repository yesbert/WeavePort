using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WeavePort.CapacityTests;

internal sealed record ResourceSample(DateTimeOffset Utc, string Phase, VmSample Vm, long HostRssBytes, long ManagedBytes,
    double HostCpuPercent, long ChildRssBytes, int ChildCount, long AppleVmProcessesRssBytes, double AppleVmProcessesCpuPercent,
    double? MacMemoryFreePercent, string? MacSwapUsage, int ThreadPoolThreads, long QueuedWorkItems,
    int Gen0Collections, int Gen1Collections, int Gen2Collections, double GcPauseMs, long AllocatedBytes, long SkippedStatsLines, int ExpectedWorkers, int MissingWorkers, WorkerSample[] Workers,
    long? CoordinatorCgroupMemoryBytes = null, string? CoordinatorMemoryEvents = null);
