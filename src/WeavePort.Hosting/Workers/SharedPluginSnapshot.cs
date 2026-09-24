using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;
/// <summary>Current shared process and slot state, including cancelled work still executing.</summary>
public sealed record SharedPluginSnapshot(int ReadyWorkers, int ActiveCalls, int AbandonedCalls, int Restarts, bool Disabled)
{
    /// <summary>Last lifecycle failure type when cleanup prevents recovery; excludes plugin text.</summary>
    public string? Failure { get; init; }
}
