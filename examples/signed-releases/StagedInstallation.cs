using WeavePort.Hosting;

// Construct at the deployment boundary, never from upload request fields.
internal sealed class StagedInstallation
{
    private readonly string _installedRoot;
    private readonly PluginPublisherPolicy _publisherPolicy;

    internal StagedInstallation(string installedRoot, PluginPublisherPolicy publisherPolicy)
    {
        ArgumentNullException.ThrowIfNull(publisherPolicy);
        if (!publisherPolicy.RequiresSignature)
        {
            throw new ArgumentException("Upload admission requires trusted publisher signatures.", nameof(publisherPolicy));
        }

        _installedRoot = Path.GetFullPath(installedRoot);
        _publisherPolicy = publisherPolicy;
    }

    // The host owns extraction, an exclusive immutable staging directory and a protected same-volume destination.
    // Installation credentials may invoke this method, but cannot replace its publisher policy.
    internal PluginInstallationVerification Install(string staging, string plugin, string version, string contract)
    {
        PluginInstallationVerification verified = PluginInstallationVerifier.Verify(staging, plugin, version, contract, _publisherPolicy);
        string parent = Path.Combine(_installedRoot, plugin, "releases");
        Directory.CreateDirectory(parent);
        Directory.Move(staging, Path.Combine(parent, version));
        return verified;
    }
}
