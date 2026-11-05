namespace WeavePort.Abstractions;
/// <summary>Host-supplied destination that creates one writer per function output.</summary>
public interface IPluginOutputDestination
{
    /// <summary>Creates the writer for a validated output name. WeavePort later commits or aborts it, then disposes it.</summary>
    ValueTask<IPluginOutputWriter> CreateAsync(string name, string mediaType, CancellationToken cancellationToken);
}
