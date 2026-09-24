namespace WeavePort.Abstractions;
/// <summary>Artifact identity disagreement observed before plugin dispatch.</summary>
/// <param name = "Expected">The host binding's required artifact version.</param>
/// <param name = "Advertised">The worker's advertised artifact version.</param>
public sealed record PluginVersionMismatch(string Expected, string Advertised);
