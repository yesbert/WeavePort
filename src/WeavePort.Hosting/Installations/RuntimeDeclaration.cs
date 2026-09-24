namespace WeavePort.Hosting;

internal sealed record RuntimeDeclaration(string Ecosystem, string Source, string Requirement, string? Sha256 = null);
