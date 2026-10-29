using System.Security.Cryptography;
using WeavePort.Hosting;

internal static class ReleasePipeline
{
    // The pipeline owns the private key and release staging directory. Do not place keys in release content.
    internal static InstallationIdentity SealAndSign(PluginSealOptions options, RSA publisherKey)
    {
        InstallationIdentity identity = PluginInstallationSealer.Seal(options);
        InstallationIdentity signed = PluginInstallationSigner.Sign(options.ReleaseDirectory,
            options.Plugin, options.Version, options.Contract, publisherKey);
        if (signed != identity)
        {
            throw new InvalidDataException("Release changed between sealing and signing.");
        }

        return signed;
    }
}
