using System.Text.Json;

namespace WeavePort.Abstractions;
/// <summary>A host-authorized callback. Tenant identity is never taken from plugin data.</summary>
/// <param name = "Context">The binding's trusted authority.</param>
/// <param name = "InvocationId">The host-generated invocation identifier.</param>
/// <param name = "Operation">Granted callback name.</param>
/// <param name = "Payload">Untrusted plugin arguments.</param>
/// <param name = "TraceId">Host trace correlation.</param>
public sealed record HostCall(PluginContext Context, string InvocationId, string Operation, JsonElement Payload, string TraceId);
