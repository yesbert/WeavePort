namespace WeavePort.Hosting;
/// <summary>Explicit local wire selection; protocol versions do not attest plugin artifact identity.</summary>
public enum ProcessProtocol
{
    /// <summary>WeavePort protocol 1 with native context and callbacks.</summary>
    Native = 0,
    /// <summary>MCP 2025-11-25 tools subset with initialization; stdio only, no fallback.</summary>
    Mcp20251125 = 1,
    /// <summary>MCP 2026-07-28 tools subset with per-request metadata; stdio only, no fallback.</summary>
    Mcp20260728 = 2
}
