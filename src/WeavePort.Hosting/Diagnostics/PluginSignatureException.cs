namespace WeavePort.Hosting;

/// <summary>A release failed publisher authentication before plugin dispatch. Codes do not imply retry safety.</summary>
public sealed class PluginSignatureException : IOException
{
    /// <summary>Gets the stable publisher refusal code.</summary>
    public string ErrorCode { get; }

    internal PluginSignatureException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
