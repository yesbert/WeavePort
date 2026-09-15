using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeavePort.Hosting;
internal sealed record McpRequest(string Jsonrpc, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id, string Method, [property: JsonPropertyName("params")] Dictionary<string, JsonElement> Parameters);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 32)]
[JsonSerializable(typeof(McpRequest))]
internal partial class McpWireJson : JsonSerializerContext;
