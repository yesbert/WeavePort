namespace WeavePort.Hosting;
/// <summary>Diagnostic result for one immediate plugin directory. Exactly one of Installation and Refusal is populated.</summary>
/// <param name = "Directory">Absolute plugin directory inspected.</param>
/// <param name = "Installation">Verified selected installation, when usable.</param>
/// <param name = "Refusal">Stable refusal category, or null on success.</param>
/// <param name = "Diagnostic">Actionable deployment diagnostic, or null on success. May contain local paths; do not expose to untrusted users.</param>
public sealed record InstallationDiscovery(string Directory, InstalledPlugin? Installation, string? Refusal, string? Diagnostic);
