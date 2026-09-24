using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeavePort.Hosting;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 32)]
[JsonSerializable(typeof(McpRequest))]
[JsonSerializable(typeof(McpResponse))]
internal partial class McpWireJson : JsonSerializerContext;
