using System.Text.Json;
using System.Text.Json.Serialization;
using WeavePort.Abstractions;

namespace WeavePort.Hosting;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InvokeFrame))]
[JsonSerializable(typeof(CallbackResultFrame))]
internal partial class WireJson : JsonSerializerContext;
