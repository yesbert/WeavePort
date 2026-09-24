using System.Text.Json;

// Independent peer: intentionally malformed wire messages exercise host validation.
internal sealed class McpFixture(string fault)
{
    private int _counter;

    internal static Task RunAsync(string fault) => new McpFixture(fault).ReadRequestsAsync();

    private async Task ReadRequestsAsync()
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var request = JsonSerializer.Deserialize<JsonElement>(line);
            if (!request.TryGetProperty("id", out var id))
            {
                continue;
            }
            if (!await HandleAsync(request, id))
            {
                return;
            }
        }
    }

    private async Task<bool> HandleAsync(JsonElement request, JsonElement id)
    {
        string method = request.GetProperty("method").GetString()!;
        if (ReplyMetadata(method, id))
        {
            return true;
        }
        string operation = request.GetProperty("params").GetProperty("name").GetString()!;
        if (operation == "hang")
        {
            await Task.Delay(Timeout.Infinite);
            return false;
        }
        if (operation == "crash")
        {
            Environment.Exit(7);
        }
        if (fault == "ping" && !await PingAsync())
        {
            return false;
        }
        if (fault == "stderr")
        {
            WriteStderr();
        }
        if (CorruptFrame(id) is { } corrupt)
        {
            Console.WriteLine(corrupt);
            return true;
        }
        if (fault == "utf8")
        {
            await Console.OpenStandardOutput().WriteAsync(new byte[] { 34, 255, 34, 10 });
            return true;
        }
        if (fault == "eof")
        {
            return false;
        }
        if (fault == "flood")
        {
            WriteNotificationFlood();
        }
        if (ReplyInteraction(id))
        {
            return true;
        }
        ReplyToolResult(request, id, operation);
        return true;
    }

    private bool ReplyMetadata(string method, JsonElement id)
    {
        if (method is "initialize" or "server/discover")
        {
            object result = method == "initialize"
                ? new
                {
                    protocolVersion = fault == "version" ? "wrong" : "2025-11-25",
                    capabilities = new
                    {
                        tools = new
                        {
                        }
                    },
                    serverInfo = new
                    {
                        name = "fixture",
                        version = "1"
                    }
                }
                : new
                {
                    supportedVersions = new[] { fault == "version" ? "wrong" : "2026-07-28" },
                    capabilities = new
                    {
                        tools = new
                        {
                        }
                    }
                };
            Reply(id, result);
            return true;
        }
        if (method != "tools/list")
        {
            return false;
        }
        Reply(id, new
        {
            tools = new[] { new { name = "echo", inputSchema = new { type = "object" } }, new { name = "counter", inputSchema = new { type = "object" } } },
            nextCursor = "next"
        });
        return true;
    }

    private static async Task<bool> PingAsync()
    {
        Console.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":\"server-ping\",\"method\":\"ping\"}");
        string? reply = await Console.In.ReadLineAsync();
        if (reply is null)
        {
            return false;
        }
        var response = JsonSerializer.Deserialize<JsonElement>(reply);
        if (response.GetProperty("id").GetString() != "server-ping" || response.GetProperty("result").EnumerateObject().Any())
        {
            throw new Exception("Invalid ping response");
        }
        return true;
    }

    private string? CorruptFrame(JsonElement id)
    {
        string rawId = id.GetRawText();
        return fault switch
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
    }

    private bool ReplyInteraction(JsonElement id)
    {
        if (fault == "interaction")
        {
            Reply(id, new
            {
                resultType = "input_required",
                inputRequests = new[] { new { method = "sampling/createMessage" } }
            });
            return true;
        }
        if (fault != "interaction-content")
        {
            return false;
        }
        Reply(id, new
        {
            resultType = "input_required",
            content = Array.Empty<object>(),
            inputRequests = new
            {
            }
        });
        return true;
    }

    private void ReplyToolResult(JsonElement request, JsonElement id, string operation)
    {
        var parameters = request.GetProperty("params");
        object value = operation == "counter" ? new
        {
            counter = ++_counter,
            pid = Environment.ProcessId
        } : (object)parameters.GetProperty("arguments");
        Reply(id, new
        {
            content = Array.Empty<object>(),
            structuredContent = value,
            isError = operation == "fail",
            received = request
        });
    }

    private static void WriteStderr()
    {
        for (int i = 0; i < 1024; i++)
        {
            Console.Error.Write(new string('x', 4096));
        }
    }

    private static void WriteNotificationFlood()
    {
        for (int i = 0; i < 34; i++)
        {
            Console.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}");
        }
    }

    private static void Reply(JsonElement id, object result) => Console.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }));
}
