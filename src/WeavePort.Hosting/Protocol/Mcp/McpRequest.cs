using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeavePort.Hosting;

internal sealed record McpRequest(string Jsonrpc, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id, string Method, [property: JsonPropertyName(McpFields.Parameters)] Dictionary<string, JsonElement> Parameters);
