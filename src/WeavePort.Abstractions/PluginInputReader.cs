using Microsoft.Win32.SafeHandles;

namespace WeavePort.Abstractions;
/// <summary>Built-in positional readers for function inputs. The caller keeps ownership of the stream or file handle.</summary>
public static class PluginInputReader
{
    /// <summary>Reads a readable, seekable stream; access is serialized because the stream position is shared.</summary>
    public static IPluginInputReader FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Function inputs require a readable, seekable stream.", nameof(stream));
        }

        return new StreamReader(stream);
    }

    /// <summary>Reads an open file handle with positional reads that do not move a shared position.</summary>
    public static IPluginInputReader FromFile(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.IsInvalid || handle.IsClosed)
        {
            throw new ArgumentException("The file handle must be open.", nameof(handle));
        }

        return new FileReader(handle);
    }

    private sealed class StreamReader(Stream stream) : IPluginInputReader
    {
        private readonly SemaphoreSlim _gate = new(1);

        public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                stream.Position = offset;
                return await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private sealed class FileReader(SafeFileHandle handle) : IPluginInputReader
    {
        public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            int filled = 0;
            while (filled < buffer.Length)
            {
                int read = await RandomAccess.ReadAsync(handle, buffer[filled..], offset + filled, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            return filled;
        }
    }
}
