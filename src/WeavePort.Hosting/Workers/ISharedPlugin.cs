using WeavePort.Sdk.Client;

namespace WeavePort.Hosting;
/// <summary>Resident shared plugin. Tenant views own no worker and cannot restart it.</summary>
public interface ISharedPlugin : IAsyncDisposable
{
    /// <summary>Returns a cheap tenant-bound client using immutable host authority.</summary>
    IBoundPluginClient For(string tenant);
    /// <summary>Observes current residency and restart state.</summary>
    SharedPluginSnapshot Snapshot { get; }
}
