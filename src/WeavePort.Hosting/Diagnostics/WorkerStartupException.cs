namespace WeavePort.Hosting;

internal sealed class WorkerStartupException(Exception primary, Exception? cleanup, bool timedOut) : IOException("Worker startup failed.", cleanup is null ? primary : new AggregateException(primary, cleanup))
{
    internal Exception Primary { get; } = primary;
    internal bool CleanupFailed => cleanup is not null;
    internal bool TimedOut { get; } = timedOut;
}
