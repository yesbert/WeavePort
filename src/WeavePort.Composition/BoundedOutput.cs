namespace WeavePort.Composition;

internal sealed class BoundedOutput(Stream target, Action<int> reserve, CancellationToken lifetime) : Stream
{
    private bool _disposed;
    private void Check(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lifetime.ThrowIfCancellationRequested();
        reserve(count);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        lifetime.ThrowIfCancellationRequested();
        target.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => target.FlushAsync(cancellationToken);
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check(buffer.Length);
        target.Write(buffer);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Check(buffer.Length);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
        await target.WriteAsync(buffer, linked.Token);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
