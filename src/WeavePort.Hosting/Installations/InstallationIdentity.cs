namespace WeavePort.Hosting;
/// <summary>Persistable identity of a trusted installation manifest, separate from domain state.</summary>
public sealed record InstallationIdentity(string Plugin, string Version, string Contract, string Digest);
