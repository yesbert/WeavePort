namespace WeavePort.Hosting;
// Stable identifiers: never renumber or reuse an event for a different meaning.
// See docs/runtime-diagnostics.md for fields, causes and recovery guidance.
internal static class RuntimeLogEvents
{
    internal const int DiagnosticDeliveryFailed = 1008;
    internal const int StartupFailed = 1001;
    internal const int CallbackFailed = 1002;
    internal const int InvocationFailed = 1003;
    internal const int CleanupFailed = 1004;
    internal const int MaintenanceFailed = 1005;
    internal const int AdmissionRejected = 1006;
    internal const int StandardError = 1007;
}
