using System.Text.Json;

namespace WeavePort.Hosting;
internal static class McpMessages
{
    private static readonly JsonElement ModernMetadata = JsonSerializer.SerializeToElement(new Dictionary<string, object> { [McpNames.ProtocolVersionMetadata] = McpNames.Revision20260728, [McpNames.ClientCapabilitiesMetadata] = new { } });
    internal static Task WriteAsync(Stream stream, string? id, string method, JsonElement parameters, string? revision, CancellationToken token)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            fields.Add(property.Name, property.Value);
        }

        if (revision is not null)
        {
            fields.Add(McpFields.Metadata, ModernMetadata);
        }

        return Frames.WriteAsync(stream, new McpRequest(McpNames.JsonRpcVersion, id, method, fields), McpWireJson.Default.McpRequest, token);
    }

    internal static void Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected MCP object.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            AddUniqueName(names, property.Name);
        }
    }

    private static void AddUniqueName(HashSet<string> names, string name)
    {
        if (!names.Add(name))
        {
            throw new InvalidDataException("Repeated MCP field.");
        }
    }

    internal static string String(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out JsonElement field) || field.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Expected MCP string field.");
        }

        return field.GetString()!;
    }

    internal static void Parameters(string method, JsonElement parameters)
    {
        Object(parameters);
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            bool valid = method switch
            {
                McpMethods.ListTools => property.NameEquals(McpFields.Cursor) && property.Value.ValueKind == JsonValueKind.String,
                McpMethods.CallTool => property.NameEquals(McpFields.Name) && property.Value.ValueKind == JsonValueKind.String || property.NameEquals(McpFields.Arguments) && property.Value.ValueKind == JsonValueKind.Object,
                _ => false
            };
            if (!valid)
            {
                throw new InvalidDataException("Unsupported MCP request parameter.");
            }
        }

        if (method is not (McpMethods.ListTools or McpMethods.CallTool) || method == McpMethods.CallTool && string.IsNullOrWhiteSpace(String(parameters, McpFields.Name)))
        {
            throw new InvalidDataException("Unsupported MCP method or tool name.");
        }
    }
}
