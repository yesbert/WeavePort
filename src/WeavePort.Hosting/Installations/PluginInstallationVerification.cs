namespace WeavePort.Hosting;

/// <summary>Offline verification result. External files, runtime compatibility and deployment stability still require host validation.</summary>
/// <param name="Identity">Exact manifest identity.</param>
/// <param name="Publisher">Authenticated publisher, or null under integrity-only policy.</param>
public sealed record PluginInstallationVerification(InstallationIdentity Identity, VerifiedPluginPublisher? Publisher);
