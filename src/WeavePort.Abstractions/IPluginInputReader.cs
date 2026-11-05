namespace WeavePort.Abstractions;
/// <summary>Host-supplied positional reader for one function input. WeavePort calls it at any offset in any order, never concurrently within one invocation and never beyond the declared length.</summary>
public interface IPluginInputReader
{
    /// <summary>Reads bytes starting at <paramref name = "offset"/>. Returns fewer bytes than requested only at the end of the input, and zero at or past the end.</summary>
    ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken);
}
