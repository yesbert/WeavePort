using System.Text.Json;

namespace WeavePort.Abstractions;
/// <summary>Host-bound identity and immutable invocation configuration.</summary>
/// <param name = "Tenant">Authenticated data owner, supplied by the trusted host.</param>
/// <param name = "Plugin">Plugin identifier.</param>
/// <param name = "Version">Resolved plugin version.</param>
/// <param name = "Profile">Configuration profile identifier.</param>
/// <param name = "Configuration">Configuration for this binding, including scoped test secrets.</param>
public sealed record PluginContext(string Tenant, string Plugin, string Version, string Profile, JsonElement Configuration);
