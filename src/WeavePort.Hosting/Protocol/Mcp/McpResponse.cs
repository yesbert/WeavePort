using System.Text.Json;
using System.Text.Json.Serialization;

namespace WeavePort.Hosting;

internal sealed record McpResponse(string Jsonrpc, JsonElement Id, JsonElement Result);
