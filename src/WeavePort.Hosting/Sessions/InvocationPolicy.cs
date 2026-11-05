namespace WeavePort.Hosting;

internal static class InvocationPolicy
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan CleanupGrace = TimeSpan.FromSeconds(10);
}
