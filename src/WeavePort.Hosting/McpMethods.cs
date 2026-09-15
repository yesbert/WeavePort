namespace WeavePort.Hosting;
/// <summary>Supported method identifiers for local MCP session invocations.</summary>
public static class McpMethods
{
    /// <summary>Lists one page of tools; pass an optional cursor in the request object.</summary>
    public const string ListTools = "tools/list";
    /// <summary>Calls a named tool with optional object arguments.</summary>
    public const string CallTool = "tools/call";
}
