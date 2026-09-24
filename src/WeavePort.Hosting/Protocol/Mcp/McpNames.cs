namespace WeavePort.Hosting;

internal static class McpNames
{
    internal const string JsonRpcVersion = "2.0";
    internal const string Revision20251125 = "2025-11-25";
    internal const string Revision20260728 = "2026-07-28";
    internal const string Initialize = "initialize";
    internal const string Discover = "server/discover";
    internal const string Ping = "ping";
    internal const string Initialized = "notifications/initialized";
    internal const string Cancelled = "notifications/cancelled";
    internal const string LogMessage = "notifications/message";
    internal const string Progress = "notifications/progress";
    internal const string ToolsChanged = "notifications/tools/list_changed";
    internal const string ProtocolVersionMetadata = "io.modelcontextprotocol/protocolVersion";
    internal const string ClientCapabilitiesMetadata = "io.modelcontextprotocol/clientCapabilities";
    internal const string CompleteResult = "complete";
}
