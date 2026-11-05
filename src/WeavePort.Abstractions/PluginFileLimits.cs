namespace WeavePort.Abstractions;
/// <summary>Host-owned file limits for one invocation.</summary>
public sealed record PluginFileLimits
{
    /// <summary>Creates validated limits.</summary>
    /// <param name = "maximumOutputs">Maximum number of outputs, 0-64; 0 permits none.</param>
    /// <param name = "maximumOutputBytes">Maximum total bytes across all outputs.</param>
    /// <param name = "maximumInputReadBytes">Maximum input bytes the host readers deliver; null means four times the sum of the declared input lengths.</param>
    public PluginFileLimits(int maximumOutputs, long maximumOutputBytes, long? maximumInputReadBytes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumOutputs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumOutputs, WeavePort.Internal.ProtocolLimits.MaximumFileOutputs);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumOutputBytes);
        if (maximumInputReadBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInputReadBytes));
        }

        MaximumOutputs = maximumOutputs;
        MaximumOutputBytes = maximumOutputBytes;
        MaximumInputReadBytes = maximumInputReadBytes;
    }

    /// <summary>Permits no outputs and uses the default input read budget.</summary>
    public static PluginFileLimits InputsOnly { get; } = new(0, 0);

    /// <summary>Maximum number of outputs.</summary>
    public int MaximumOutputs { get; }

    /// <summary>Maximum total bytes across all outputs.</summary>
    public long MaximumOutputBytes { get; }

    /// <summary>Explicit input read budget, or null for the default.</summary>
    public long? MaximumInputReadBytes { get; }
}
