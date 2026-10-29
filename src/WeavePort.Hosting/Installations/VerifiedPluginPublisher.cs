namespace WeavePort.Hosting;

/// <summary>Publisher authentication observed at verification time; not a live revocation check.</summary>
/// <param name="Publisher">Host-configured publisher label.</param>
/// <param name="KeyId">Verified public-key fingerprint.</param>
public sealed record VerifiedPluginPublisher(string Publisher, string KeyId);
