using System.Text.Json;

// A bounded NDJSON parser and top-K reduction in the product simulator, not a semantic reranker.
internal sealed class DocumentReducer(int documents, int textBytes, int branches) : Stream
{
    private const int TopResultCount = 20;
    private const int JsonMetadataBytes = 128;
    private readonly byte[] _line = new byte[textBytes + JsonMetadataBytes];
    private readonly PriorityQueue<int, int> _top = new();
    private int _used;
    private int _rows;
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        foreach (byte value in buffer)
        {
            if (value != '\n')
            {
                AppendByte(value);
                continue;
            }

            CompleteRow();
        }
    }

    private void AppendByte(byte value)
    {
        if (_used == _line.Length)
        {
            throw new InvalidDataException("Oversized document.");
        }

        _line[_used++] = value;
    }

    private void CompleteRow()
    {
        DocumentRow row = JsonSerializer.Deserialize(_line.AsSpan(0, _used), WorkloadJson.Default.DocumentRow)
            ?? throw new InvalidDataException("Missing document.");
        if (row.Id != _rows % documents || row.Score != row.Id ||
            row.Text.Length != textBytes || row.Text.AsSpan().ContainsAnyExcept('A'))
        {
            throw new InvalidDataException("Document content/order mismatch.");
        }

        RetainTopScore(row);
        _rows++;
        _used = 0;
    }

    private void RetainTopScore(DocumentRow row)
    {
        // Identical fixture branches: deduplicate before global top-K selection.
        if (_rows >= documents)
        {
            return;
        }

        _top.Enqueue(row.Id, row.Score);
        if (_top.Count > TopResultCount)
        {
            _top.Dequeue();
        }
    }

    internal void Complete()
    {
        if (_used != 0 || _rows != documents * branches)
        {
            throw new InvalidDataException("Incomplete document list.");
        }
    }
    internal int[] TopIds() => _top.UnorderedItems.Select(x => x.Element).OrderDescending().ToArray();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public override bool CanRead => false;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException(); set => throw new NotSupportedException();
    }
    public override void Flush()
    {
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
