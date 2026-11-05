using System.Security.Cryptography;
using System.Text.Json;

// Raw protocol-1 worker that runs a scripted list of file callbacks from the invocation payload.
// Each step is {"operation": "...", "payload": {...}}, or {"op": "hang"} / {"op": "fail"}.
internal static class FileWorker
{
    private static readonly object Failed = new();

    internal static async Task RunAsync(bool advertise)
    {
        Console.WriteLine(advertise
            ? "{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\",\"files\":1}"
            : "{\"type\":\"ready\",\"protocol\":1,\"pluginVersion\":\"1\"}");
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var request = JsonDocument.Parse(line);
            await RunScriptAsync(request.RootElement);
        }
    }

    private static async Task RunScriptAsync(JsonElement request)
    {
        string id = request.GetProperty("id").GetString()!;
        JsonElement script = request.GetProperty("payload");
        if (request.GetProperty("operation").GetString() == "$sdk.call")
        {
            script = script.GetProperty("input");
        }

        var replies = new List<object?>();
        int callback = 0;
        foreach (JsonElement step in script.GetProperty("steps").EnumerateArray())
        {
            object? reply = await RunStepAsync(id, step, ++callback);
            if (reply == Failed)
            {
                return;
            }

            replies.Add(reply);
        }

        JsonElement? files = request.TryGetProperty("files", out JsonElement declared) ? declared : null;
        Write(new
        {
            type = "result",
            id,
            value = new
            {
                files,
                replies,
                pad = new string('x', script.TryGetProperty("pad", out JsonElement pad) ? pad.GetInt32() : 0)
            }
        });
    }

    private static async Task<object?> RunStepAsync(string id, JsonElement step, int callback)
    {
        string? control = step.TryGetProperty("op", out JsonElement op) ? op.GetString() : null;
        if (control == "hang")
        {
            await Task.Delay(Timeout.Infinite);
        }

        if (control == "fail")
        {
            Write(new
            {
                type = "error",
                id,
                code = "sdk-error"
            });
            return Failed;
        }

        Write(new
        {
            type = "callback",
            id,
            callbackId = callback.ToString(),
            operation = step.GetProperty("operation").GetString(),
            payload = step.GetProperty("payload")
        });
        using var reply = JsonDocument.Parse((await Console.In.ReadLineAsync())!);
        return Summarize(reply.RootElement);
    }

    private static object? Summarize(JsonElement reply)
    {
        if (reply.TryGetProperty("error", out JsonElement error))
        {
            return new
            {
                error = error.GetString()
            };
        }

        JsonElement value = reply.GetProperty("value");
        if (value.TryGetProperty("data", out JsonElement data))
        {
            byte[] bytes = data.GetBytesFromBase64();
            return new
            {
                sha = Convert.ToHexString(SHA256.HashData(bytes)),
                length = bytes.Length
            };
        }

        return value.Clone();
    }

    private static void Write(object frame) => Console.WriteLine(JsonSerializer.Serialize(frame));
}
