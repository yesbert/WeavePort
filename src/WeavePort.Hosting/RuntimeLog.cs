using Microsoft.Extensions.Logging;

namespace WeavePort.Hosting;
// Event IDs 1001–1007 belong to WeavePort.Hosting. Deliberately omit Exception
// parameters: messages, stack traces and inner errors can contain customer data.
internal static partial class RuntimeLog
{
    // Raw plugin content is only passed here after explicit operator opt-in.
    [LoggerMessage(1007, LogLevel.Information, "Worker stderr: {WorkerInstance} {DiagnosticLine}")]
    internal static partial void StandardError(ILogger logger, string workerInstance, string diagnosticLine);
    [LoggerMessage(1001, LogLevel.Warning, "Worker startup failed: {WorkerInstance} {ErrorType}")]
    internal static partial void StartupFailed(ILogger logger, string workerInstance, string errorType);
    [LoggerMessage(1002, LogLevel.Warning, "Host callback failed: {WorkerInstance} {CorrelationId} {ErrorType}")]
    internal static partial void CallbackFailed(ILogger logger, string workerInstance, string correlationId, string errorType);
    [LoggerMessage(1003, LogLevel.Warning, "Invocation failed: {WorkerInstance} {CorrelationId} {Stage} {ErrorType}")]
    internal static partial void InvocationFailed(ILogger logger, string workerInstance, string correlationId, string stage, string errorType);
    [LoggerMessage(1004, LogLevel.Error, "Cleanup failed: {WorkerInstance} {ErrorType}")]
    internal static partial void CleanupFailed(ILogger logger, string workerInstance, string errorType);
    [LoggerMessage(1005, LogLevel.Error, "Maintenance failed: {ErrorType}")]
    internal static partial void MaintenanceFailed(ILogger logger, string errorType);
    [LoggerMessage(1006, LogLevel.Warning, "Admission rejected: {Reason}")]
    internal static partial void AdmissionRejected(ILogger logger, string reason);
}
