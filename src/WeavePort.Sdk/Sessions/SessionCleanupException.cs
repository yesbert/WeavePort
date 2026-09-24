using WeavePort.Internal;

namespace WeavePort.Sdk;
internal sealed class SessionCleanupException(IEnumerable<Exception> errors) : AggregateException("Session cleanup failed.", errors)
{
    internal bool HasExecutionFailure { get; init; }
    internal string ErrorCode => FailureCodes.CleanupError;
}
