namespace WeavePort.Abstractions;
/// <summary>A binding that can reserve exclusive worker residency for a multi-exchange operation.</summary>
public interface IPluginOperationSession : IPluginSession
{
    /// <summary>Whether this binding permits stateful result streams and binary sources.</summary>
    bool SupportsStreaming { get; }

    /// <summary>Waits for an operation reservation. Dispose the returned session to release the reservation, not the binding.</summary>
    ValueTask<IPluginSession> AcquireOperationAsync(CancellationToken cancellationToken = default);
}
