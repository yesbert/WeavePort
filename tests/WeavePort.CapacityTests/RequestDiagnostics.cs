using System.Diagnostics;

namespace WeavePort.CapacityTests;

internal sealed record RequestObservation(double StartedMs, double CompletedMs, string Status,
    double WriteMs, double SerializeMs, double ReadMs, double FirstByteMs, double ParseMs, int Reads, double HostStartedUtcMs, double HostCompletedUtcMs, System.Text.Json.JsonElement? WorkerTiming);

internal sealed class RequestDiagnostics : IDisposable
{
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("WEAVEPORT_DIAGNOSTICS") == "1";
    internal static readonly int PayloadChars = ReadInt("WEAVEPORT_PAYLOAD_CHARS", 65536, 1, 262144);
    private static readonly AsyncLocal<RequestDiagnostics?> Current = new();
    private readonly RequestDiagnostics? _previous;
    private double _writeMs, _serializeMs, _readMs, _firstByteMs, _parseMs;
    private int _reads;

    internal RequestDiagnostics()
    {
        _previous = Current.Value;
        Current.Value = this;
    }

    internal static int ReadInt(string name, int fallback, int minimum, int maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (value is null) return fallback;
        if (!int.TryParse(value, out int number) || number < minimum || number > maximum) throw new ArgumentException(name);
        return number;
    }

    internal static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => Enabled && source.Name == "WeavePort.Transport",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Current.Value is not { } request) return;
                double Tag(string key) => Convert.ToDouble(activity.GetTagItem(key) ?? 0);
                if (activity.OperationName == "frame.write")
                {
                    request._writeMs += Tag("complete.ms");
                    request._serializeMs += Tag("serialize.ms");
                }
                else
                {
                    request._readMs += Tag("complete.ms");
                    request._firstByteMs += Tag("first-byte.ms");
                    request._parseMs += Tag("parse.ms");
                    request._reads += (int)Tag("reads");
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    internal RequestObservation Finish(double start, double end, string status, double startUtc, double endUtc, System.Text.Json.JsonElement value) =>
        new(start, end, status, _writeMs, _serializeMs, _readMs, _firstByteMs, _parseMs, _reads, startUtc, endUtc,
            value.ValueKind == System.Text.Json.JsonValueKind.Object && value.TryGetProperty("_diagnostic", out var timing) ? timing.Clone() : null);

    public void Dispose() => Current.Value = _previous;
}
