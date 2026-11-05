using WeavePort.Abstractions;
using WeavePort.Internal;

namespace WeavePort.Hosting;
// A file operation the plugin may recover from; reported as a callback-result error code.
internal sealed class FileRefusal(string code) : Exception("File operation refused: " + code)
{
    internal string Code { get; } = code;
}

// A file condition that fails the whole invocation with a stable failure code.
internal class PluginFileException(string code, Exception? inner = null) : Exception("Function file failure: " + code, inner)
{
    internal string Code { get; } = code;
}

internal sealed class OutputCommitException(IReadOnlyList<PluginOutputInfo> committed, Exception inner) : PluginFileException(FailureCodes.OutputCommitFailed, inner)
{
    internal IReadOnlyList<PluginOutputInfo> Committed { get; } = committed;
}
