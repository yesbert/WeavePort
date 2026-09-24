namespace WeavePort.Sdk.Client;
/// <summary>An immutable value and its host invocation duration, excluding gateway transport. Null means timing was unavailable.</summary>
/// <param name = "Value">The decoded operation value.</param>
/// <param name = "ElapsedMs">Host-measured elapsed milliseconds for this invocation, or null.</param>
public sealed record PluginCallResult<T>(T Value, double? ElapsedMs);
