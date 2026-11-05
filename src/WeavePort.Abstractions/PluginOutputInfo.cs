namespace WeavePort.Abstractions;
/// <summary>A committed function output.</summary>
/// <param name = "Name">Output name chosen by the plugin.</param>
/// <param name = "MediaType">Media type declared by the plugin.</param>
/// <param name = "Length">Exact committed length in bytes.</param>
public sealed record PluginOutputInfo(string Name, string MediaType, long Length);
