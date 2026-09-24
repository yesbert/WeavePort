using Microsoft.Extensions.Logging;

namespace WeavePort.Hosting;
// Event IDs 1001–1008 belong to WeavePort.Hosting. Deliberately omit Exception
// parameters: messages, stack traces and inner errors can contain customer data.
internal static partial class RuntimeLog
{
    [LoggerMessage(RuntimeLogEvents.DiagnosticDeliveryFailed, LogLevel.Warning, "Detailed diagnostic destination failed: {ErrorType}")]
    internal static partial void DiagnosticDeliveryFailed(ILogger logger, string errorType);
    // Raw plugin content is only passed here after explicit operator opt-in.
    [LoggerMessage(RuntimeLogEvents.StandardError, LogLevel.Information, "Worker stderr: {WorkerInstance} {DiagnosticLine}")]
    internal static partial void StandardError(ILogger logger, string workerInstance, string diagnosticLine);
    [LoggerMessage(RuntimeLogEvents.StartupFailed, LogLevel.Warning, "Worker startup failed: {WorkerInstance} {ErrorType}")]
    internal static partial void StartupFailed(ILogger logger, string workerInstance, string errorType);
    [LoggerMessage(RuntimeLogEvents.CallbackFailed, LogLevel.Warning, "Host callback failed: {WorkerInstance} {CorrelationId} {ErrorType}")]
    internal static partial void CallbackFailed(ILogger logger, string workerInstance, string correlationId, string errorType);
    [LoggerMessage(RuntimeLogEvents.InvocationFailed, LogLevel.Warning, "Invocation failed: {WorkerInstance} {CorrelationId} {Stage} {ErrorType} {Code} CleanupFailed={CleanupFailed}")]
    internal static partial void InvocationFailed(ILogger logger, string workerInstance, string correlationId, string stage, string errorType, string code, bool cleanupFailed);
    [LoggerMessage(RuntimeLogEvents.CleanupFailed, LogLevel.Error, "Cleanup failed: {WorkerInstance} {ErrorType}")]
    internal static partial void CleanupFailed(ILogger logger, string workerInstance, string errorType);
    [LoggerMessage(RuntimeLogEvents.MaintenanceFailed, LogLevel.Error, "Maintenance failed: {ErrorType}")]
    internal static partial void MaintenanceFailed(ILogger logger, string errorType);
    [LoggerMessage(RuntimeLogEvents.AdmissionRejected, LogLevel.Warning, "Admission rejected: {Reason}")]
    internal static partial void AdmissionRejected(ILogger logger, string reason);
}
