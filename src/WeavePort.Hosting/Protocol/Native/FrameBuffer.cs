using System.Buffers;
using System.Security.Cryptography;

namespace WeavePort.Hosting;
// Serialization completes within the limit before any bytes reach the worker.
internal sealed class FrameBuffer : Stream
{
    private const int InitialBufferBytes = 4096;
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialBufferBytes);
    private int _length;
    internal ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _length);
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > Frames.MaximumBytes - _length)
        {
            throw new InvalidDataException("Frame exceeds limit.");
        }

        int needed = _length + buffer.Length;
        if (needed > _buffer.Length)
        {
            byte[] next = ArrayPool<byte>.Shared.Rent(Math.Max(needed, Math.Min(_buffer.Length * 2, Frames.MaximumBytes)));
            _buffer.AsSpan(0, _length).CopyTo(next);
            CryptographicOperations.ZeroMemory(_buffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = next;
        }

        buffer.CopyTo(_buffer.AsSpan(_length));
        _length = needed;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (_buffer.Length != 0)
        {
            CryptographicOperations.ZeroMemory(_buffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        base.Dispose(disposing);
    }
}
