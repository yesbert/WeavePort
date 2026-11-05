namespace WeavePort.Abstractions;
/// <summary>A named input the host hands to one function invocation. The plugin sees name, length and media type; the bytes pass only through the reader.</summary>
public sealed record PluginInput
{
    /// <summary>Creates a validated input descriptor.</summary>
    /// <param name = "name">1-128 characters from A-Z, a-z, 0-9, '.', '_' and '-', not starting with '.'; never a path.</param>
    /// <param name = "length">Exact input length in bytes; fixed for the invocation.</param>
    /// <param name = "mediaType">Opaque media type passed to the plugin, 1-255 visible ASCII characters.</param>
    /// <param name = "reader">Host-owned positional reader; the caller retains ownership.</param>
    public PluginInput(string name, long length, string mediaType, IPluginInputReader reader)
    {
        Name = WeavePort.Internal.FileDescriptor.Name(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Length = length;
        MediaType = WeavePort.Internal.FileDescriptor.MediaType(mediaType, nameof(mediaType));
        Reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>Input name, unique within the invocation's inputs.</summary>
    public string Name { get; }

    /// <summary>Exact length in bytes.</summary>
    public long Length { get; }

    /// <summary>Opaque media type.</summary>
    public string MediaType { get; }

    /// <summary>Host-owned positional reader.</summary>
    public IPluginInputReader Reader { get; }
}
