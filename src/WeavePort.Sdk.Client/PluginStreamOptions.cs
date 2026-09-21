namespace WeavePort.Sdk.Client;
/// <summary>Independent deadlines for each wire exchange and the complete stream, including consumer pauses.</summary>
public sealed record PluginStreamOptions
{
    /// <summary>Maximum duration of one start, next, read or close exchange.</summary>
    public TimeSpan ExchangeTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Maximum lifetime from enumeration start until completion.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        if (ExchangeTimeout <= TimeSpan.Zero || ExchangeTimeout.TotalMilliseconds > uint.MaxValue - 1 || TotalTimeout <= TimeSpan.Zero || TotalTimeout.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PluginStreamOptions));
        }
    }
}
