using System.Security.Cryptography;

namespace WeavePort.Composition;
/// <summary>An immutable result belonging exclusively to its creating request scope. Contains no filesystem path.</summary>
public sealed class ResultHandle
{
    internal ResultHandle(string id, long length)
    {
        Id = id;
        Length = length;
    }

    internal string Id { get; }
    /// <summary>Gets the committed byte length.</summary>
    public long Length { get; }
}
