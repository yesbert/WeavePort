internal sealed class BlockingSink : Stream
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Writes { get; private set; }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Writes++;
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException(); public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }
    public override void Flush() => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
