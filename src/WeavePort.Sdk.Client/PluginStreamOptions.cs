namespace WeavePort.Sdk.Client;
/// <summary>Independent deadlines for each wire exchange and the complete stream, including consumer pauses.</summary>
public sealed record PluginStreamOptions
{
    // CancellationTokenSource timers reserve uint.MaxValue for an infinite timeout.
    private const double MaximumTimeoutMilliseconds = uint.MaxValue - 1;
    /// <summary>Maximum duration of one start, next, read or close exchange.</summary>
    public TimeSpan ExchangeTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Maximum lifetime from enumeration start until completion.</summary>
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromMinutes(5);

    internal void Validate() => Validate(this);
    private static void Validate(PluginStreamOptions options)
    {
        if (!IsSupportedTimeout(options.ExchangeTimeout) || !IsSupportedTimeout(options.TotalTimeout))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Stream exchange and total timeouts must be positive and at most 4294967294 milliseconds.");
        }
    }

    private static bool IsSupportedTimeout(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout.TotalMilliseconds <= MaximumTimeoutMilliseconds;
}
