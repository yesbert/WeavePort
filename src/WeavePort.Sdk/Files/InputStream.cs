using WeavePort.Internal;

namespace WeavePort.Sdk;
// Read-only seekable view of a host input. Reads fetch block-aligned blocks and keep the most recently used ones,
// so archive readers that revisit their directory and headers do not cause another round trip.
internal sealed class InputStream(FileSession files, PluginFileInfo info) : Stream
{
    private readonly LinkedList<(long Index, byte[] Data)> _cache = new();
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => info.Length;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        SeekOrigin.End => info.Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin))
    };

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        // Checked on every read, including cache hits, so a retained stream cannot outlive its invocation.
        files.ThrowIfClosed();
        int total = 0;
        while (buffer.Length > 0 && _position < info.Length)
        {
            byte[] block = await BlockAsync(_position / files.BlockBytes, cancellationToken);
            int start = (int)(_position % files.BlockBytes);
            int count = Math.Min(block.Length - start, buffer.Length);
            if (count <= 0)
            {
                break;
            }

            block.AsMemory(start, count).CopyTo(buffer);
            buffer = buffer[count..];
            _position += count;
            total += count;
        }

        return total;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // Synchronous reads serve libraries without async APIs; the SDK runtime has no synchronization context to deadlock on.
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private async Task<byte[]> BlockAsync(long index, CancellationToken token)
    {
        LinkedListNode<(long Index, byte[] Data)>? cached = _cache.First;
        while (cached is not null && cached.Value.Index != index)
        {
            cached = cached.Next;
        }

        if (cached is not null)
        {
            _cache.Remove(cached);
            _cache.AddFirst(cached);
            return cached.Value.Data;
        }

        byte[] data = await files.ReadAsync(info.Name, index * files.BlockBytes, token);
        _cache.AddFirst((index, data));
        if (_cache.Count > ProtocolLimits.FileBlockCacheBlocks)
        {
            _cache.RemoveLast();
        }

        return data;
    }
}
