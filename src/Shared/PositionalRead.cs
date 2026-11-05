namespace WeavePort.Internal;
// Fills a buffer from a positional input reader, which may return fewer bytes than requested only at its end.
internal static class PositionalRead
{
    internal static async Task FillAsync(WeavePort.Abstractions.IPluginInputReader reader, long offset, Memory<byte> destination, CancellationToken token)
    {
        int filled = 0;
        while (filled < destination.Length)
        {
            int read = await reader.ReadAsync(offset + filled, destination[filled..], token);
            if (read <= 0 || read > destination.Length - filled)
            {
                throw new IOException("Input reader did not honour the declared input length.");
            }

            filled += read;
        }
    }
}
