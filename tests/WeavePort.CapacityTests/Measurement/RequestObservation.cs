using System.Diagnostics;

namespace WeavePort.CapacityTests;

internal sealed record RequestObservation(double StartedMs, double CompletedMs, string Status,
    double WriteMs, double SerializeMs, double ReadMs, double FirstByteMs, double ParseMs, int Reads, double HostStartedUtcMs, double HostCompletedUtcMs, System.Text.Json.JsonElement? WorkerTiming);
