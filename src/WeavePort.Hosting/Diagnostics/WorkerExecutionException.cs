using WeavePort.Internal;

namespace WeavePort.Hosting;
internal sealed class WorkerExecutionException(string code, bool cleanupFailed) : IOException("Plugin reported an execution failure.")
{
    internal string Code { get; } = FailureCode.Normalize(code);
    internal bool CleanupFailed { get; } = cleanupFailed;
}
