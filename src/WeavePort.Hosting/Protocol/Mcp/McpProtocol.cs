using System.Text.Json;

namespace WeavePort.Hosting;

internal sealed partial class McpProtocol(ProcessProtocol protocol)
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private readonly string _revision = protocol == ProcessProtocol.Mcp20251125 ? McpNames.Revision20251125 : McpNames.Revision20260728;
    private bool _writingResponse;
    private bool Modern => protocol == ProcessProtocol.Mcp20260728;

    internal async Task InitializeAsync(Worker worker, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        JsonElement parameters = Modern ? Empty : JsonSerializer.SerializeToElement(new
        {
            protocolVersion = _revision,
            capabilities = new
            {
            },
            clientInfo = new
            {
                name = "WeavePort",
                version = "1"
            }
        });
        JsonElement result = await RequestAsync(worker, id, Modern ? McpNames.Discover : McpNames.Initialize, parameters, token);
        bool compatible = Modern ? result.TryGetProperty(McpFields.SupportedVersions, out JsonElement versions) && versions.ValueKind == JsonValueKind.Array && versions.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == _revision) : McpMessages.ReadString(result, McpFields.ProtocolVersion) == _revision;
        if (!compatible || !result.TryGetProperty(McpFields.Capabilities, out JsonElement capabilities))
        {
            throw new InvalidDataException("Unsupported MCP server revision or capabilities.");
        }

        McpMessages.RequireObject(capabilities);
        if (!capabilities.TryGetProperty(McpFields.Tools, out JsonElement tools))
        {
            throw new InvalidDataException("MCP server does not offer tools.");
        }

        McpMessages.RequireObject(tools);
        if (!Modern)
        {
            McpMessages.RequireObject(result.GetProperty(McpFields.ServerInfo));
            await McpMessages.WriteAsync(worker.Input, null, McpNames.Initialized, Empty, null, token);
        }
    }

    internal async Task<JsonElement> InvokeAsync(Worker worker, string id, string method, JsonElement parameters, CancellationToken token)
    {
        McpMessages.ValidateParameters(method, parameters);
        JsonElement result = await RequestAsync(worker, id, method, parameters, token);
        if (method == McpMethods.ListTools)
        {
            ValidateList(result);
            return result;
        }

        if (!result.TryGetProperty(McpFields.Content, out JsonElement content) || content.ValueKind != JsonValueKind.Array || result.TryGetProperty(McpFields.IsError, out JsonElement error) && error.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Unsupported MCP tool result or interaction.");
        }

        return result;
    }

    private static void ValidateList(JsonElement result)
    {
        if (!result.TryGetProperty(McpFields.Tools, out JsonElement tools) || tools.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Invalid MCP tools list.");
        }

        if (result.TryGetProperty(McpFields.NextCursor, out JsonElement cursor) && cursor.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Invalid MCP cursor.");
        }

        foreach (JsonElement tool in tools.EnumerateArray())
        {
            McpMessages.RequireObject(tool);
            _ = McpMessages.ReadString(tool, McpFields.Name);
            McpMessages.RequireObject(tool.GetProperty(McpFields.InputSchema));
        }
    }

}
