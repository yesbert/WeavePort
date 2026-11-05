namespace WeavePort.Sdk;
/// <summary>A host-named input of the current invocation.</summary>
/// <param name = "Name">Input name chosen by the host.</param>
/// <param name = "Length">Exact length in bytes.</param>
/// <param name = "MediaType">Opaque media type supplied by the host.</param>
public sealed record PluginFileInfo(string Name, long Length, string MediaType);
