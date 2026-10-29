namespace WeavePort.Hosting;

internal sealed record InstallationRelease(InstalledPluginCatalog.Manifest Manifest, byte[] Bytes, InstallationIdentity Identity, VerifiedPluginPublisher? Publisher);
