namespace WeavePort.Sdk;
/// <summary>A refused file operation, such as an unknown input or an output that is not permitted. The invocation continues unless the handler rethrows.</summary>
public sealed class PluginFileException : IOException
{
    internal PluginFileException(string code) : base("Function file operation refused: " + code) => Code = code;

    /// <summary>Stable refusal code: input-not-found, output-not-permitted, output-exists, output-closed or invalid-range.</summary>
    public string Code { get; }
}
