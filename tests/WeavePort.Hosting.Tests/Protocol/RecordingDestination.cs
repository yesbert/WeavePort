using System.Collections.Concurrent;
using WeavePort.Abstractions;

// Records every writer event so checks can assert commit, abort and disposal exactly.
internal sealed class RecordingDestination(string? failCommitFor = null) : IPluginOutputDestination
{
    internal ConcurrentQueue<string> Events { get; } = new();
    internal ConcurrentDictionary<string, MemoryStream> Data { get; } = new();

    public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken)
    {
        Events.Enqueue($"create:{name}:{mediaType}");
        var data = new MemoryStream();
        Data[name] = data;
        return ValueTask.FromResult<IPluginOutputWriter>(new Writer(this, name, data, name == failCommitFor));
    }

    private sealed class Writer(RecordingDestination owner, string name, MemoryStream data, bool failCommit) : IPluginOutputWriter
    {
        public ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken)
        {
            data.Write(block.Span);
            return ValueTask.CompletedTask;
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            owner.Events.Enqueue("commit:" + name);
            return failCommit ? ValueTask.FromException(new IOException("commit failed")) : ValueTask.CompletedTask;
        }

        public ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken)
        {
            owner.Events.Enqueue($"abort:{name}:{reason}");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owner.Events.Enqueue("dispose:" + name);
            return ValueTask.CompletedTask;
        }
    }
}
