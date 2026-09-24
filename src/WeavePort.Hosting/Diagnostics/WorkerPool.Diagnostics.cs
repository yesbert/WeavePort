using WeavePort.Abstractions;

namespace WeavePort.Hosting;

internal sealed partial class WorkerPool
{
    internal void ReportFailure(string instance, PluginFailure failure, Exception error)
    {
        RuntimeLog.InvocationFailed(logger, instance, failure.CorrelationId, failure.Phase, error.GetType().Name, failure.Code, failure.CleanupFailed);
        try
        {
            options.DetailedFailureSink?.Invoke(new FailureDiagnostic(instance, failure, error));
        }
        catch (Exception delivery) when (delivery is not OutOfMemoryException)
        {
            RuntimeLog.DiagnosticDeliveryFailed(logger, delivery.GetType().Name);
        }
    }
}
