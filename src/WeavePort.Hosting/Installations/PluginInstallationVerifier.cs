namespace WeavePort.Hosting;

/// <summary>Verifies a staged schema-2 release offline without probing runtimes or executing plugin code.</summary>
public static class PluginInstallationVerifier
{
    /// <summary>Authenticates and validates complete bundle content against expected identity. Runtime compatibility and external files are checked later by the catalog. Keep staging immutable through promotion.</summary>
    public static PluginInstallationVerification Verify(string releaseDirectory, string plugin, string version, string contract,
        PluginPublisherPolicy publisherPolicy, InstallationIdentity? pinned = null)
    {
        ArgumentNullException.ThrowIfNull(publisherPolicy);
        InstallationRelease release = InstalledPluginCatalog.ReadValidatedRelease(releaseDirectory, plugin, version, contract, publisherPolicy, pinned);
        PublisherSignature.RequirePortable(release.Manifest.Schema);
        return new(release.Identity, release.Publisher);
    }
}
