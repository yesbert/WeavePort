using System.Text.Json;

internal static class McpFixture
{
    internal static async Task RunAsync(string fault)
    {
        int counter = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var request = JsonSerializer.Deserialize<JsonElement>(line);
            if (!request.TryGetProperty("id", out var id)) continue;
            string method = request.GetProperty("method").GetString()!;
            if (method is "initialize" or "server/discover")
            {
                object result = method == "initialize"
                    ? new { protocolVersion = fault == "version" ? "wrong" : "2025-11-25", capabilities = new { tools = new { } }, serverInfo = new { name = "fixture", version = "1" } }
                    : new { supportedVersions = new[] { fault == "version" ? "wrong" : "2026-07-28" }, capabilities = new { tools = new { } } };
                Reply(id, result);
                continue;
            }
            if (method == "tools/list")
            {
                Reply(id, new { tools = new[] { new { name = "echo", inputSchema = new { type = "object" } }, new { name = "counter", inputSchema = new { type = "object" } } }, nextCursor = "next" });
                continue;
            }
            string operation = request.GetProperty("params").GetProperty("name").GetString()!;
            if (operation == "hang") { await Task.Delay(Timeout.Infinite); return; }
            if (operation == "crash") Environment.Exit(7);
            if (fault == "ping")
            {
                Console.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":\"server-ping\",\"method\":\"ping\"}");
                string? reply = await Console.In.ReadLineAsync();
                if (reply is null) return;
                var response = JsonSerializer.Deserialize<JsonElement>(reply);
                if (response.GetProperty("id").GetString() != "server-ping" || response.GetProperty("result").EnumerateObject().Any()) throw new Exception("Invalid ping response");
            }
            if (fault == "stderr") { for (int i = 0; i < 1024; i++) Console.Error.Write(new string('x', 4096)); }
            string rawId = id.GetRawText();
            string? corrupt = fault switch
            {
                "duplicate" => "{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"\\u0069d\":" + rawId + ",\"result\":{}}",
                "wrong-id" => "{\"jsonrpc\":\"2.0\",\"id\":\"foreign\",\"result\":{}}",
                "both" => "{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"result\":{},\"error\":{}}",
                "request" => "{\"jsonrpc\":\"2.0\",\"id\":" + rawId + ",\"method\":\"sampling/createMessage\",\"params\":{}}",
                "depth" => new string('[', 33) + "0" + new string(']', 33),
                "oversize" => new string(' ', 1048577),
                "malformed" => "not-json",
                _ => null
            };
            if (corrupt is not null) { Console.WriteLine(corrupt); continue; }
            if (fault == "utf8") { await Console.OpenStandardOutput().WriteAsync(new byte[] { 34, 255, 34, 10 }); continue; }
            if (fault == "eof") return;
            if (fault == "flood") { for (int i = 0; i < 34; i++) Console.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}"); }
            if (fault == "interaction") { Reply(id, new { resultType = "input_required", inputRequests = new[] { new { method = "sampling/createMessage" } } }); continue; }
            if (fault == "interaction-content") { Reply(id, new { resultType = "input_required", content = Array.Empty<object>(), inputRequests = new { } }); continue; }
            var parameters = request.GetProperty("params");
            object value = operation == "counter" ? new { counter = ++counter, pid = Environment.ProcessId } : (object)parameters.GetProperty("arguments");
            Reply(id, new { content = Array.Empty<object>(), structuredContent = value, isError = operation == "fail", received = request });
        }
    }
    private static void Reply(JsonElement id, object result) => Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
}
