namespace WeavePort.Sdk.Client;
/// <summary>Independent deadlines for each wire exchange and the complete stream, including consumer pauses.</summary>
public sealed record PluginStreamOptions
{
    /// <summary>Maximum duration of one start, next, read or close exchange.</summary>
    public TimeSpan ExchangeTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Maximum lifetime from enumeration start until completion.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromMinutes(5);

    internal void Validate() => Validate(this);
    private static void Validate(PluginStreamOptions options)
    {
        if (options.ExchangeTimeout <= TimeSpan.Zero || options.ExchangeTimeout.TotalMilliseconds > uint.MaxValue - 1 || options.TotalTimeout <= TimeSpan.Zero || options.TotalTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Stream exchange and total timeouts must be positive and at most 4294967294 milliseconds.");
        }
    }
}
