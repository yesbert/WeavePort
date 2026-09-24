using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

if (args.Length != 2 || !Path.IsPathFullyQualified(args[0]) || !Path.IsPathFullyQualified(args[1]))
{
    throw new ArgumentException("Pass absolute paths to node and the MCP server script.");
}

await using var host = new PluginHost();
foreach (ProcessProtocol protocol in new[] { ProcessProtocol.Mcp20251125, ProcessProtocol.Mcp20260728 })
{
    var profile = new ProcessProfile(args[0], [args[1]], trustedCode: true, timeout: TimeSpan.FromSeconds(5))
    {
        Protocol = protocol
    };
    var context = new PluginContext("department-a", "document-tools", "1", "local-mcp",
        JsonSerializer.SerializeToElement(new
        {
        }));
    await using var session = await host.BindAsync(context, profile, new NoCallbacks(), []);
    InvocationResult list = await session.InvokeAsync(McpMethods.ListTools, JsonSerializer.SerializeToElement(new
    {
    }));
    EnsureSuccess(list);
    if (!list.Value.GetProperty("tools").EnumerateArray().Any(tool => tool.GetProperty("name").GetString() == "normalize"))
    {
        throw new InvalidOperationException("The server did not advertise normalize.");
    }

    InvocationResult result = await session.InvokeAsync(McpMethods.CallTool, JsonSerializer.SerializeToElement(new
    {
        name = "normalize",
        arguments = new
        {
            text = " hello   world "
        }
    }));
    EnsureSuccess(result);
    string? text = result.Value.GetProperty("structuredContent").GetProperty("text").GetString();
    if (text != "hello world")
    {
        throw new InvalidOperationException("Unexpected normalized text.");
    }

    Console.WriteLine($"{protocol}: {text}");
}

static void EnsureSuccess(InvocationResult result)
{
    if (result.Status != "ok")
    {
        throw new InvalidOperationException($"Host exchange failed: {result.Status}");
    }

    if (result.Value.TryGetProperty("isError", out JsonElement error) && error.GetBoolean())
    {
        throw new InvalidOperationException("The tool reported an application error.");
    }
}

sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        => throw new NotSupportedException("This MCP binding has no callbacks.");
}
