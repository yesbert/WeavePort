internal sealed class CheckingSink(byte[] pattern) : Stream
{
    internal long Count { get; private set; }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        foreach (byte item in buffer)
        {
            if (item != pattern[Count % pattern.Length])
            {
                throw new InvalidDataException("Caller content mismatch.");
            }
            Count++;
        }
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => false;
    public override long Length => Count; public override long Position
    {
        get => Count; set => throw new NotSupportedException();
    }
    public override void Flush()
    {
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
