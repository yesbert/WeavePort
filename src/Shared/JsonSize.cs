using System.Text.Json;

namespace WeavePort.Internal;
// Count the serializer's actual escaped UTF-8 output without allocating a result array.
// Each operation owns its sink; no tenant data or mutable writer is shared.
internal sealed class JsonSize : Stream
{
    private long _length;
    internal static long Measure(JsonElement value)
    {
        using var sink = new JsonSize();
        JsonSerializer.Serialize(sink, value);
        return sink.Length;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer) => _length = checked(_length + buffer.Length);
    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
