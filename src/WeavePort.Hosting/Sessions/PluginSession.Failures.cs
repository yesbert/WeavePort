using System.Diagnostics;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
internal sealed partial class PluginSession
{
    private string ClassifyFailure(Exception error, CancellationToken caller, CancellationToken deadline)
    {
        if (error is WorkerStartupException startup)
        {
            return startup.TimedOut ? FailureCodes.Timeout : ClassifyFailure(startup.Primary, caller, deadline);
        }

        if (error is not OperationCanceledException)
        {
            return ClassifyException(error);
        }

        if (caller.IsCancellationRequested)
        {
            return FailureCodes.Cancelled;
        }

        if (_lifetime.IsCancellationRequested)
        {
            return FailureCodes.Disabled;
        }

        if (deadline.IsCancellationRequested)
        {
            return FailureCodes.Timeout;
        }

        return FailureCodes.InternalError;
    }

    private static string ClassifyException(Exception error) => error switch
    {
        WorkerExecutionException execution => execution.Code,
        PluginVersionMismatchException => FailureCodes.VersionMismatch,
        UnauthorizedAccessException => FailureCodes.Denied,
        InvalidDataException or JsonException => FailureCodes.ProtocolError,
        IOException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException => FailureCodes.Failed,
        _ => FailureCodes.InternalError
    };
    private async Task<InvocationResult> CompleteFailureAsync(Exception primary, string code, string id, long started, Activity? activity)
    {
        string phase = _dispatched ? FailurePhases.Exchange : FailurePhases.Prepare;
        Exception details = primary;
        bool cleanupFailed = primary is WorkerExecutionException { CleanupFailed: true } or WorkerStartupException { CleanupFailed: true };
        try
        {
            await StopAsync();
        }
        catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
        {
            cleanupFailed = true;
            details = new AggregateException("Execution and worker cleanup failed.", primary, cleanup);
        }

        var failure = new PluginFailure(code, phase, id, cleanupFailed);
        pool.ReportFailure(Instance, failure, details);
        if (code != FailureCodes.Cancelled && code != FailureCodes.Disabled)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("error.type", code);
        }

        activity?.SetTag("weaveport.correlation_id", id);
        string status = primary is WorkerExecutionException ? FailureCodes.Failed : code;
        return Result(status, started)with
        {
            Failure = failure,
            VersionMismatch = (primary as PluginVersionMismatchException)?.Mismatch
        };
    }
}
