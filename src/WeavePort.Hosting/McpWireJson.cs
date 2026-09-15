using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeavePort.Hosting;
internal sealed record McpRequest(string Jsonrpc, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id, string Method, [property: JsonPropertyName("params")] Dictionary<string, JsonElement> Parameters);
internal sealed record McpResponse(string Jsonrpc, JsonElement Id, JsonElement Result);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, MaxDepth = 32)]
[JsonSerializable(typeof(McpRequest))]
[JsonSerializable(typeof(McpResponse))]
internal partial class McpWireJson : JsonSerializerContext;
