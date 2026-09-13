using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;
using WeavePort.Composition;

internal sealed record DocumentRow(int Id, int Score, string Text);
internal sealed record WorkloadResult(string Tenant, int Documents, int Branches, long InputBytes, long IntermediateBytes,
    int[] TopIds, double ComputeMs, double AiMs);
[JsonSerializable(typeof(DocumentRow))]
[JsonSerializable(typeof(WorkloadResult))]
internal partial class WorkloadJson : JsonSerializerContext;

internal static class DocumentWorkload
{
    internal static async Task<WorkloadResult> RunAsync(string root, string tenant, IPluginSession[] sessions,
        WorkloadOptions options, Stream? export, CancellationToken token)
    {
        long began = Stopwatch.GetTimestamp();
        await using var scope = new ResultScope(root, tenant, new ResultLimits(256L << 20, 1L << 30, 16));
        ResultHandle input = await scope.CreateAsync(async (stream, ct) =>
        {
            byte[] newline = [10];
            string text = new('a', options.TextBytes);
            for (int id = 0; id < options.Documents; id++)
            {
                byte[] row = JsonSerializer.SerializeToUtf8Bytes(new DocumentRow(id, id, text), WorkloadJson.Default.DocumentRow);
                await stream.WriteAsync(row, ct);
                await stream.WriteAsync(newline, ct);
            }
        }, token);
        ResultHandle result = input;
        int branches = options.Kind is "rag" or "large" ? 3 : 1;
        if (branches == 3)
        {
            var steps = sessions.Select<IPluginSession, Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>>(session =>
                (owner, handle, ct) => Composition.MapAsync(owner, handle, session, 65536, ct)).ToArray();
            ResultHandle[] results = await Composition.FanOutAsync(scope, input, steps, 3, token);
            result = await Composition.ConcatenateAsync(scope, results, token);
        }
        else
        {
            foreach (IPluginSession session in sessions.Take(options.Kind == "search" ? 1 : 3))
                result = await Composition.MapAsync(scope, result, session, 65536, token);
        }
        using var reducer = new DocumentReducer(options.Documents, options.TextBytes, branches);
        await scope.CopyToAsync(result, reducer, token);
        reducer.Complete();
        if (export is not null)
            await scope.CopyToAsync(result, export, token);
        double compute = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
        long ai = Stopwatch.GetTimestamp();
        if (options.AiMs > 0)
            await Task.Delay(options.AiMs, token);
        return new(tenant, options.Documents, branches, input.Length, result.Length,
            reducer.TopIds(), compute, Stopwatch.GetElapsedTime(ai).TotalMilliseconds);
    }
}

// A bounded NDJSON parser and top-K reduction in the product simulator, not a semantic reranker.
internal sealed class DocumentReducer(int documents, int textBytes, int branches) : Stream
{
    private readonly byte[] _line = new byte[textBytes + 128];
    private readonly PriorityQueue<int, int> _top = new();
    private int _used;
    private int _rows;
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        foreach (byte value in buffer)
        {
            if (value != '\n')
            {
                if (_used == _line.Length) throw new InvalidDataException("Oversized document.");
                _line[_used++] = value;
                continue;
            }
            DocumentRow row = JsonSerializer.Deserialize(_line.AsSpan(0, _used), WorkloadJson.Default.DocumentRow)
                ?? throw new InvalidDataException("Missing document.");
            if (row.Id != _rows % documents || row.Score != row.Id || row.Text.Length != textBytes || row.Text.AsSpan().ContainsAnyExcept('A'))
                throw new InvalidDataException("Document content/order mismatch.");
            // Identical fixture branches: deduplicate before global top-K selection.
            if (_rows < documents)
            {
                _top.Enqueue(row.Id, row.Score);
                if (_top.Count > 20) _top.Dequeue();
            }
            _rows++;
            _used = 0;
        }
    }
    internal void Complete()
    {
        if (_used != 0 || _rows != documents * branches) throw new InvalidDataException("Incomplete document list.");
    }
    internal int[] TopIds() => _top.UnorderedItems.Select(x => x.Element).OrderDescending().ToArray();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Write(buffer.Span); return ValueTask.CompletedTask;
    }
    public override bool CanRead => false;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed record WorkloadOptions(string Kind, int Documents, int TextBytes, int AiMs);
