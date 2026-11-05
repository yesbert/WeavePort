namespace WeavePort.Sdk;
/// <summary>Write-only output of the current invocation. Disposing completes it; <see cref = "CompleteAsync"/> completes it with explicit error reporting. The host commits it only if the invocation succeeds.</summary>
public sealed class PluginOutputStream : Stream
{
    private readonly FileSession _files;
    private readonly byte[] _buffer;
    private int _count;
    private long _written;
    private bool _completed;

    internal PluginOutputStream(FileSession files, string name)
    {
        _files = files;
        Name = name;
        _buffer = new byte[files.BlockBytes];
    }

    /// <summary>Output name.</summary>
    public string Name { get; }

    /// <inheritdoc/>
    public override bool CanRead => false;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => !_completed;
    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <summary>Bytes written so far; setting is not supported.</summary>
    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        while (buffer.Length > 0)
        {
            int count = Math.Min(_buffer.Length - _count, buffer.Length);
            buffer[..count].CopyTo(_buffer.AsMemory(_count));
            _count += count;
            _written += count;
            buffer = buffer[count..];
            await SendFullBlockAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <summary>Sends the remaining bytes and completes the output. Repeated calls do nothing.</summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        if (_count > 0)
        {
            await _files.WriteAsync(Name, _buffer.AsMemory(0, _count), cancellationToken);
            _count = 0;
        }

        await _files.CompleteAsync(Name, cancellationToken);
    }

    /// <summary>Buffered bytes are sent in full blocks and on completion; flushing sends nothing early.</summary>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CompleteAsync();
        await base.DisposeAsync();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CompleteAsync().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    private async Task SendFullBlockAsync(CancellationToken token)
    {
        if (_count < _buffer.Length)
        {
            return;
        }

        await _files.WriteAsync(Name, _buffer, token);
        _count = 0;
    }
}
