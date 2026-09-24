using System.Collections.ObjectModel;

namespace WeavePort.Hosting;
/// <summary>A validated local installation. Deployment files must remain unchanged during execution.</summary>
public sealed class InstalledPlugin
{
    /// <summary>Gets the identity to persist with recoverable application state.</summary>
    public InstallationIdentity Identity { get; }
    /// <summary>Gets the resolved, verified entry points by trusted alias.</summary>
    public IReadOnlyDictionary<string, string> EntryPoints { get; }

    private readonly PluginLaunchDeclaration? _launch;
    /// <summary>Gets a copy of the verified launch declaration, when supplied by the installation.</summary>
    public PluginLaunchDeclaration? Launch => _launch?.Freeze();
    internal IReadOnlyDictionary<string, string> RuntimeFiles { get; }
    internal RuntimeValidation? RuntimeValidation { get; init; }

    internal InstalledPlugin(InstallationIdentity identity, Dictionary<string, string> entries, Dictionary<string, string> runtimes, PluginLaunchDeclaration? launch)
    {
        Identity = identity;
        EntryPoints = new ReadOnlyDictionary<string, string>(entries);
        RuntimeFiles = new ReadOnlyDictionary<string, string>(runtimes);
        _launch = launch?.Freeze();
    }
}
