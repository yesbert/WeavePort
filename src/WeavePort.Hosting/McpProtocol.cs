using System.Text.Json;

namespace WeavePort.Hosting;
internal sealed class McpProtocol(ProcessProtocol protocol)
{
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });
    private readonly string _revision = protocol == ProcessProtocol.Mcp20251125 ? McpNames.Revision20251125 : McpNames.Revision20260728;
    private bool _writingResponse;
    private bool Modern => protocol == ProcessProtocol.Mcp20260728;

    internal async Task InitializeAsync(Worker worker, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        JsonElement parameters = Modern ? Empty : JsonSerializer.SerializeToElement(new { protocolVersion = _revision, capabilities = new { }, clientInfo = new { name = "WeavePort", version = "1" } });
        JsonElement result = await RequestAsync(worker, id, Modern ? McpNames.Discover : McpNames.Initialize, parameters, token);
        bool compatible = Modern ? result.TryGetProperty(McpFields.SupportedVersions, out JsonElement versions) && versions.ValueKind == JsonValueKind.Array && versions.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == _revision) : McpMessages.String(result, McpFields.ProtocolVersion) == _revision;
        if (!compatible || !result.TryGetProperty(McpFields.Capabilities, out JsonElement capabilities))
        {
            throw new InvalidDataException("Unsupported MCP server revision or capabilities.");
        }

        McpMessages.Object(capabilities);
        if (!capabilities.TryGetProperty(McpFields.Tools, out JsonElement tools))
        {
            throw new InvalidDataException("MCP server does not offer tools.");
        }

        McpMessages.Object(tools);
        if (!Modern)
        {
            McpMessages.Object(result.GetProperty(McpFields.ServerInfo));
            await McpMessages.WriteAsync(worker.Input, null, McpNames.Initialized, Empty, null, token);
        }
    }

    internal async Task<JsonElement> InvokeAsync(Worker worker, string id, string method, JsonElement parameters, CancellationToken token)
    {
        McpMessages.Parameters(method, parameters);
        JsonElement result = await RequestAsync(worker, id, method, parameters, token);
        if (method == McpMethods.ListTools)
        {
            ValidateList(result);
        }
        else
        {
            if (!result.TryGetProperty(McpFields.Content, out JsonElement content) || content.ValueKind != JsonValueKind.Array || result.TryGetProperty(McpFields.IsError, out JsonElement error) && error.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException("Unsupported MCP tool result or interaction.");
            }
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
            McpMessages.Object(tool);
            _ = McpMessages.String(tool, McpFields.Name);
            McpMessages.Object(tool.GetProperty(McpFields.InputSchema));
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
        catch (OperationCanceledException) when (sent && !_writingResponse && method != McpNames.Initialize)
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
            await McpMessages.WriteAsync(input, null, McpNames.Cancelled, JsonSerializer.SerializeToElement(new { requestId = id }), null, deadline.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
        // Cancellation notification is best effort; the session still destroys this worker.
        }
    }

    private async Task RespondToPingAsync(Worker worker, JsonElement frame, CancellationToken token)
    {
        if (!frame.TryGetProperty(McpFields.Id, out JsonElement id) || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number) || frame.TryGetProperty(McpFields.Result, out _) || frame.TryGetProperty(McpFields.Error, out _))
        {
            throw new InvalidDataException("Invalid MCP ping request.");
        }

        if (frame.TryGetProperty(McpFields.Parameters, out JsonElement parameters))
        {
            McpMessages.Object(parameters);
        }

        _writingResponse = true;
        await Frames.WriteAsync(worker.Input, new McpResponse(McpNames.JsonRpcVersion, id, Empty), McpWireJson.Default.McpResponse, token);
        _writingResponse = false;
    }

    private async Task<JsonElement> ReceiveAsync(Worker worker, string id, CancellationToken token)
    {
        for (int notifications = 0; notifications <= 32; notifications++)
        {
            JsonElement frame = await worker.Reader.ReadAsync(token);
            McpMessages.Object(frame);
            if (McpMessages.String(frame, McpFields.JsonRpc) != McpNames.JsonRpcVersion)
            {
                throw new InvalidDataException("Invalid MCP JSON-RPC version.");
            }

            if (frame.TryGetProperty(McpFields.Method, out _))
            {
                if (notifications == 32)
                {
                    throw new InvalidDataException("MCP notification budget exceeded.");
                }

                await HandleServerMessageAsync(worker, frame, token);
                continue;
            }

            return ReadResponse(frame, id);
        }

        throw new InvalidDataException("MCP notification budget exceeded.");
    }

    private async Task HandleServerMessageAsync(Worker worker, JsonElement frame, CancellationToken token)
    {
        string method = McpMessages.String(frame, McpFields.Method);
        if (!Modern && method == McpNames.Ping)
        {
            await RespondToPingAsync(worker, frame, token);
            return;
        }

        if (frame.TryGetProperty(McpFields.Id, out _) || frame.TryGetProperty(McpFields.Result, out _) || frame.TryGetProperty(McpFields.Error, out _) || method is not (McpNames.LogMessage or McpNames.Progress or McpNames.ToolsChanged))
        {
            throw new InvalidDataException("Unsupported MCP server interaction.");
        }

        if (frame.TryGetProperty(McpFields.Parameters, out JsonElement parameters))
        {
            McpMessages.Object(parameters);
        }
    }

    private static JsonElement ReadResponse(JsonElement frame, string id)
    {
        if (McpMessages.String(frame, McpFields.Id) != id || frame.TryGetProperty(McpFields.Parameters, out _) || frame.TryGetProperty(McpFields.Result, out _) == frame.TryGetProperty(McpFields.Error, out _))
        {
            throw new InvalidDataException("MCP response correlation or envelope violation.");
        }

        if (frame.TryGetProperty(McpFields.Error, out JsonElement error))
        {
            McpMessages.Object(error);
            if (!error.TryGetProperty(McpFields.Code, out JsonElement code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out _))
            {
                throw new InvalidDataException("Invalid MCP error code.");
            }

            _ = McpMessages.String(error, McpFields.Message);
            throw new IOException("MCP server reported a request error.");
        }

        JsonElement result = frame.GetProperty(McpFields.Result);
        McpMessages.Object(result);
        if (result.TryGetProperty(McpFields.ResultType, out JsonElement resultType) && (resultType.ValueKind != JsonValueKind.String || resultType.GetString() != McpNames.CompleteResult))
        {
            throw new InvalidDataException("Unsupported MCP result continuation.");
        }

        return result;
    }
}
