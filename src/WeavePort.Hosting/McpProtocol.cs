using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed class McpProtocol(ProcessProtocol protocol)
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private readonly string _revision = protocol == ProcessProtocol.Mcp20251125 ? "2025-11-25" : "2026-07-28";
    private bool _writingResponse;
    private bool Modern => protocol == ProcessProtocol.Mcp20260728;

    internal async Task InitializeAsync(Worker worker, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        JsonElement parameters = Modern ? Empty : JsonSerializer.SerializeToElement(new { protocolVersion = _revision, capabilities = new { }, clientInfo = new { name = "WeavePort", version = "1" } });
        JsonElement result = await RequestAsync(worker, id, Modern ? "server/discover" : "initialize", parameters, token);
        bool compatible = Modern ? result.TryGetProperty("supportedVersions", out JsonElement versions) && versions.ValueKind == JsonValueKind.Array && versions.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == _revision) : McpMessages.String(result, "protocolVersion") == _revision;
        if (!compatible || !result.TryGetProperty("capabilities", out JsonElement capabilities))
        {
            throw new InvalidDataException("Unsupported MCP server revision or capabilities.");
        }

        McpMessages.Object(capabilities);
        if (!capabilities.TryGetProperty("tools", out JsonElement tools))
        {
            throw new InvalidDataException("MCP server does not offer tools.");
        }

        McpMessages.Object(tools);
        if (!Modern)
        {
            McpMessages.Object(result.GetProperty("serverInfo"));
            await McpMessages.WriteAsync(worker.Input, null, "notifications/initialized", Empty, null, token);
        }
    }

    internal async Task<JsonElement> InvokeAsync(Worker worker, string id, string method, JsonElement parameters, CancellationToken token)
    {
        McpMessages.Parameters(method, parameters);
        JsonElement result = await RequestAsync(worker, id, method, parameters, token);
        if (method == "tools/list")
        {
            ValidateList(result);
        }
        else
        {
            if (!result.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array || result.TryGetProperty("isError", out JsonElement error) && error.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("Unsupported MCP tool result or interaction.");
            }
        }

        return result;
    }

    private static void ValidateList(JsonElement result)
    {
        if (!result.TryGetProperty("tools", out JsonElement tools) || tools.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Invalid MCP tools list.");
        }

        if (result.TryGetProperty("nextCursor", out JsonElement cursor) && cursor.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Invalid MCP cursor.");
        }

        foreach (JsonElement tool in tools.EnumerateArray())
        {
            McpMessages.Object(tool);
            _ = McpMessages.String(tool, "name");
            McpMessages.Object(tool.GetProperty("inputSchema"));
        }
    }

    private async Task<JsonElement> RequestAsync(Worker worker, string id, string method, JsonElement parameters, CancellationToken token)
    {
        bool sent = false;
        try
        {
            await McpMessages.WriteAsync(worker.Input, id, method, parameters, Modern ? _revision : null, token);
            sent = true;
            return await ReceiveAsync(worker, id, token);
        }
        catch (OperationCanceledException) when (sent && !_writingResponse && method != "initialize")
        {
            await CancelAsync(worker.Input, id);
            throw;
        }
    }

    private static async Task CancelAsync(Stream input, string id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await McpMessages.WriteAsync(input, null, "notifications/cancelled", JsonSerializer.SerializeToElement(new { requestId = id }), null, deadline.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
        // Cancellation notification is best effort; the session still destroys this worker.
        }
    }

    private async Task RespondToPingAsync(Worker worker, JsonElement frame, CancellationToken token)
    {
        if (!frame.TryGetProperty("id", out JsonElement id) || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || frame.TryGetProperty("result", out _) || frame.TryGetProperty("error", out _))
        {
            throw new InvalidDataException("Invalid MCP ping request.");
        }

        if (frame.TryGetProperty("params", out JsonElement parameters))
        {
            McpMessages.Object(parameters);
        }

        _writingResponse = true;
        await Frames.WriteAsync(worker.Input, new McpResponse("2.0", id, Empty), McpWireJson.Default.McpResponse, token);
        _writingResponse = false;
    }

    private async Task<JsonElement> ReceiveAsync(Worker worker, string id, CancellationToken token)
    {
        for (int notifications = 0; notifications <= 32; notifications++)
        {
            JsonElement frame = await worker.Reader.ReadAsync(token);
            McpMessages.Object(frame);
            if (McpMessages.String(frame, "jsonrpc") != "2.0")
            {
                throw new InvalidDataException("Invalid MCP JSON-RPC version.");
            }

            if (frame.TryGetProperty("method", out _))
            {
                if (notifications == 32)
                {
                    throw new InvalidDataException("MCP notification budget exceeded.");
                }

                string method = McpMessages.String(frame, "method");
                if (!Modern && method == "ping")
                {
                    await RespondToPingAsync(worker, frame, token);
                    continue;
                }

                if (frame.TryGetProperty("id", out _) || frame.TryGetProperty("result", out _) || frame.TryGetProperty("error", out _) || method is not ("notifications/message" or "notifications/progress" or "notifications/tools/list_changed"))
                {
                    throw new InvalidDataException("Unsupported MCP server interaction.");
                }

                if (frame.TryGetProperty("params", out JsonElement parameters))
                {
                    McpMessages.Object(parameters);
                }

                continue;
            }

            if (McpMessages.String(frame, "id") != id || frame.TryGetProperty("params", out _) || frame.TryGetProperty("result", out _) == frame.TryGetProperty("error", out _))
            {
                throw new InvalidDataException("MCP response correlation or envelope violation.");
            }

            if (frame.TryGetProperty("error", out JsonElement error))
            {
                McpMessages.Object(error);
                if (!error.TryGetProperty("code", out JsonElement code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out _))
                {
                    throw new InvalidDataException("Invalid MCP error code.");
                }

                _ = McpMessages.String(error, "message");
                throw new IOException("MCP server reported a request error.");
            }

            JsonElement result = frame.GetProperty("result");
            McpMessages.Object(result);
            if (result.TryGetProperty("resultType", out JsonElement resultType) && (resultType.ValueKind != JsonValueKind.String || resultType.GetString() != "complete"))
            {
                throw new InvalidDataException("Unsupported MCP result continuation.");
            }

            return result;
        }

        throw new InvalidDataException("MCP notification budget exceeded.");
    }
}
