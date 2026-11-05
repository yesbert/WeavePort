namespace WeavePort.Abstractions;
/// <summary>Receives one function output. Writes are sequential and never concurrent. WeavePort calls <see cref = "CommitAsync"/> or <see cref = "AbortAsync"/>, then <see cref = "IAsyncDisposable.DisposeAsync"/>. Exception: when <see cref = "CommitAsync"/> fails, the same writer next receives <see cref = "AbortAsync"/> with <see cref = "PluginOutputAbort.CommitFailed"/>, so it can remove a partially committed output. Abort and disposal are bounded by a cleanup grace period; a step still running after it is reported as a cleanup failure.</summary>
public interface IPluginOutputWriter : IAsyncDisposable
{
    /// <summary>Appends the next block. The memory is valid only until the returned task completes.</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> block, CancellationToken cancellationToken);
    /// <summary>Makes the output durable after the invocation succeeded with every output completed.</summary>
    ValueTask CommitAsync(CancellationToken cancellationToken);
    /// <summary>Discards the output, including one the plugin completed, because the invocation did not succeed.</summary>
    ValueTask AbortAsync(PluginOutputAbort reason, CancellationToken cancellationToken);
}
