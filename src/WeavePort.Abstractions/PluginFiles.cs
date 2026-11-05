namespace WeavePort.Abstractions;
/// <summary>Files for one function invocation: named inputs, an optional output destination and their limits. Readers and the destination remain owned by the caller.</summary>
public sealed class PluginFiles
{
    /// <summary>Creates a validated set of files.</summary>
    /// <param name = "inputs">Up to 64 inputs with unique names.</param>
    /// <param name = "outputs">Destination for outputs; null permits none.</param>
    /// <param name = "limits">Limits for this invocation; required with a destination that should accept outputs.</param>
    public PluginFiles(IEnumerable<PluginInput> inputs, IPluginOutputDestination? outputs = null, PluginFileLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        PluginInput[] copied = [.. inputs];
        if (copied.Length > WeavePort.Internal.ProtocolLimits.MaximumFileInputs)
        {
            throw new ArgumentException("At most 64 inputs are permitted per invocation.", nameof(inputs));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (PluginInput input in copied)
        {
            if (input is null || !names.Add(input.Name))
            {
                throw new ArgumentException("Inputs must be non-null with unique names.", nameof(inputs));
            }
        }

        Limits = limits ?? PluginFileLimits.InputsOnly;
        if (outputs is not null && Limits.MaximumOutputs == 0)
        {
            throw new ArgumentException("An output destination requires limits that permit at least one output.", nameof(limits));
        }

        Inputs = Array.AsReadOnly(copied);
        Outputs = outputs;
    }

    /// <summary>Named inputs in declaration order.</summary>
    public IReadOnlyList<PluginInput> Inputs { get; }

    /// <summary>Output destination, or null when outputs are not permitted.</summary>
    public IPluginOutputDestination? Outputs { get; }

    /// <summary>Limits for this invocation.</summary>
    public PluginFileLimits Limits { get; }

    /// <summary>Effective input read budget: the explicit limit, or four times the sum of the input lengths, saturating at <see cref = "long.MaxValue"/>.</summary>
    public long InputReadBudget
    {
        get
        {
            if (Limits.MaximumInputReadBytes is long explicitBudget)
            {
                return explicitBudget;
            }

            long total = 0;
            foreach (PluginInput input in Inputs)
            {
                if (input.Length > (long.MaxValue - total) / WeavePort.Internal.ProtocolLimits.FileInputReadBudgetFactor)
                {
                    return long.MaxValue;
                }

                total += input.Length * WeavePort.Internal.ProtocolLimits.FileInputReadBudgetFactor;
            }

            return total;
        }
    }
}
