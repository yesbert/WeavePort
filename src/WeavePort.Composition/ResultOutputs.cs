using WeavePort.Abstractions;

namespace WeavePort.Composition;
/// <summary>A function output destination backed by a result scope. Outputs count against the scope's quotas, become results only when the host commits them and are deleted when it aborts them.</summary>
public sealed class ResultOutputs : IPluginOutputDestination
{
    private readonly ResultScope _scope;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ResultHandle> _results = new(StringComparer.Ordinal);

    internal ResultOutputs(ResultScope scope) => _scope = scope;

    /// <summary>Committed outputs by output name.</summary>
    public IReadOnlyDictionary<string, ResultHandle> Results => _results;

    /// <inheritdoc/>
    public ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken) => ValueTask.FromResult<IPluginOutputWriter>(new Writer(this, name, _scope.BeginOutput(cancellationToken)));

    private sealed class Writer(ResultOutputs owner, string name, ResultScope.OutputReservation pending) : IPluginOutputWriter
    {
        public ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken) => pending.WriteAsync(block, cancellationToken);

        public async ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            owner._results[name] = await pending.CommitAsync();
        }

        public ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken)
        {
            pending.Abort(new IOException("Function output aborted: " + reason));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => pending.DisposeAsync();
    }
}
