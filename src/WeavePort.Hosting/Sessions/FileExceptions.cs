using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
// A file operation the plugin may recover from; reported as a callback-result error code.
internal sealed class FileRefusalException(string code) : IOException("File operation refused: " + code)
{
    internal string Code { get; } = code;
}

// A file condition that fails the whole invocation with a stable failure code.
internal class PluginFileException(string code, Exception? inner = null) : IOException("Function file failure: " + code, inner)
{
    internal string Code { get; } = code;
}

// A file failure after some outputs were already committed; the committed ones are reported with the failure.
internal class CommittedOutputsException(string code, IReadOnlyList<PluginOutputInfo> committed, Exception? inner = null) : PluginFileException(code, inner)
{
    internal IReadOnlyList<PluginOutputInfo> Committed { get; } = committed;
}

internal sealed class OutputCommitException(IReadOnlyList<PluginOutputInfo> committed, Exception inner) : CommittedOutputsException(FailureCodes.OutputCommitFailed, committed, inner);

// Every output committed, but releasing a writer failed or exceeded the grace period.
internal sealed class OutputCleanupException(IReadOnlyList<PluginOutputInfo> committed) : CommittedOutputsException(FailureCodes.CleanupError, committed);
