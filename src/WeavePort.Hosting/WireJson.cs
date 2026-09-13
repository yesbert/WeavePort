using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;
internal sealed record InvokeFrame(string Type, string Id, string Operation, JsonElement Payload, PluginContext Context, string TraceId);
internal sealed record CallbackResultFrame(string Type, string Id, string CallbackId, JsonElement Value);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InvokeFrame))]
[JsonSerializable(typeof(CallbackResultFrame))]
internal partial class WireJson : JsonSerializerContext;
