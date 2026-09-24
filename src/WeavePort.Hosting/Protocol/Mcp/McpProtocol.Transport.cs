using System.Text.Json;

namespace WeavePort.Hosting;

internal sealed partial class McpProtocol
{
    private const int MaximumInterleavedNotifications = 32;
    private static readonly TimeSpan CancellationNotificationTimeout = TimeSpan.FromMilliseconds(100);

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
        using var deadline = new CancellationTokenSource(CancellationNotificationTimeout);
        try
        {
            await McpMessages.WriteAsync(input, null, McpNames.Cancelled, JsonSerializer.SerializeToElement(new
            {
                requestId = id
            }), null, deadline.Token);
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
            McpMessages.RequireObject(parameters);
        }

        _writingResponse = true;
        await Frames.WriteAsync(worker.Input, new McpResponse(McpNames.JsonRpcVersion, id, Empty), McpWireJson.Default.McpResponse, token);
        _writingResponse = false;
    }

    private async Task<JsonElement> ReceiveAsync(Worker worker, string id, CancellationToken token)
    {
        for (int notifications = 0; notifications <= MaximumInterleavedNotifications; notifications++)
        {
            JsonElement frame = await worker.Reader.ReadAsync(token);
            McpMessages.RequireObject(frame);
            if (McpMessages.ReadString(frame, McpFields.JsonRpc) != McpNames.JsonRpcVersion)
            {
                throw new InvalidDataException("Invalid MCP JSON-RPC version.");
            }

            if (!frame.TryGetProperty(McpFields.Method, out _))
            {
                return ReadResponse(frame, id);
            }

            if (notifications == MaximumInterleavedNotifications)
            {
                throw new InvalidDataException("MCP notification budget exceeded.");
            }

            await HandleServerMessageAsync(worker, frame, token);
        }

        throw new InvalidDataException("MCP notification budget exceeded.");
    }

    private async Task HandleServerMessageAsync(Worker worker, JsonElement frame, CancellationToken token)
    {
        string method = McpMessages.ReadString(frame, McpFields.Method);
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
            McpMessages.RequireObject(parameters);
        }
    }

    private static JsonElement ReadResponse(JsonElement frame, string id)
    {
        if (McpMessages.ReadString(frame, McpFields.Id) != id || frame.TryGetProperty(McpFields.Parameters, out _) || frame.TryGetProperty(McpFields.Result, out _) == frame.TryGetProperty(McpFields.Error, out _))
        {
            throw new InvalidDataException("MCP response correlation or envelope violation.");
        }

        if (frame.TryGetProperty(McpFields.Error, out JsonElement error))
        {
            return RejectErrorResponse(error);
        }

        JsonElement result = frame.GetProperty(McpFields.Result);
        McpMessages.RequireObject(result);
        if (result.TryGetProperty(McpFields.ResultType, out JsonElement resultType) && (resultType.ValueKind != JsonValueKind.String || resultType.GetString() != McpNames.CompleteResult))
        {
            throw new InvalidDataException("Unsupported MCP result continuation.");
        }

        return result;
    }

    private static JsonElement RejectErrorResponse(JsonElement error)
    {
        McpMessages.RequireObject(error);
        if (!error.TryGetProperty(McpFields.Code, out JsonElement code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out _))
        {
            throw new InvalidDataException("Invalid MCP error code.");
        }

        _ = McpMessages.ReadString(error, McpFields.Message);
        throw new IOException("MCP server reported a request error.");
    }
}
