## Context

The native process worker expects a WeavePort ready frame and the session sends native invocation frames. The local MCP experiment established that ordinary functions can share one MCP server process, but used a separate Node client lifecycle. See proposal.md for motivation. The source resolves .NET SDK 10.0.401, net10.0 and C# 14.

## Goals / Non-Goals

Keep one lifecycle owner and avoid new dependencies or background work for native users. Support a documented tools subset over local stdio. Do not imply full MCP feature parity or protection against hostile same-user code. Remote MCP, exported gateways, resources, prompts, sampling, elicitation, subscriptions, tasks and native callback translation are outside this change.

## Decisions

- Explicit process-profile protocol selection defaults to native. Preserve the native ready/dispatch path. MCP uses the existing bounded Frames reader/writer and session deadline, worker pool and cleanup. No nested SDK-managed processes or automatic protocol probes outside admission.
- Implement the small supported JSON-RPC subset using existing System.Text.Json facilities in Hosting. This adds no runtime dependency graph or separate package just for configuration. An external SDK was considered: its wider feature set and independent process lifecycle add cost and integration complexity for this bounded scope. Interoperability tests must use the official SDK so a fixture that merely mirrors our implementation cannot prove compatibility.
- Expose tools/list and tools/call through the existing low-level IPluginSession operation/payload API. Preserve complete MCP results, including content, structuredContent and isError. Host success denotes a completed protocol exchange; consumers still inspect tool isError. Do not claim native author SDK or LocalPluginClient stream compatibility.
- Validate exact response correlation and unambiguous envelopes. Bound frames to the existing 1 MiB and depth 32. Bound notifications per exchange; answer bounded legacy keepalive pings and reject other unsolicited requests and unsupported interaction results. Never execute returned text, follow resource URLs, or turn tool annotations into grants.
- Send only explicitly supplied tool arguments. Context.Configuration, tenant identity, callback grants and secrets are not added to MCP messages. Reject nonempty native callback grants for MCP bindings before registration. Applications own tool selection and business authorization.
- Cancellation stops the owned worker after a bounded best-effort cancellation notification, and never retries an uncertain tools/call automatically. Preserve dispatched uncertainty and existing quarantine accounting. One broken instance must not restart another tenant's instance.
- Keep all new support claims beside their tested limits. Native stdio and Unix socket behavior remain unchanged; MCP configuration initially permits stdio only. Artifact version remains a deployment identity, distinct from MCP protocol revision; MCP metadata is not artifact integrity attestation.

- Support exactly MCP 2025-11-25 and 2026-07-28 through explicit profile values. The former initializes, the latter discovers and sends metadata per request. No negotiation fallback is performed.

## Risks / Trade-offs

- Additional protocol maintenance: explicit revisions, official interoperability fixtures and fail-closed unsupported revisions.
- SDK costs are not pure wire costs: separate native regression measurements from matched MCP workloads and report process startup, latency and memory scope.
- Local processes remain trusted: no malicious-memory or escaped-process containment claims, and no Docker/service reconfiguration for this work.
- Result payloads and discovery metadata are untrusted application data. Do not infer authorization or reliability from server descriptions.

## Migration Plan

Existing consumers retain native defaults. Opt-in consumers configure the protocol explicitly and use the MCP methods documented in the new guide. Removing that configuration requires a native-speaking plugin, not automatic fallback. No package version allocation or release is part of this change.

## References

- https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio
- https://modelcontextprotocol.io/specification/2026-07-28/server/tools
- https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializer.serializetoutf8bytes?view=net-10.0
- docs/dotnet-guidance.md, docs/worker-lifecycle.md, docs/architecture.md
- Local experiment: artifacts/mcp-spike-20260915/README.md (not distribution evidence).

## Verification outcome

The unchanged-source baseline and changed-source SDK suite each passed 382 checks and 24 warm timing cells. Short-run callback/large-payload slowdowns prompted isolated controls with longer warmup, reversed order and the original Hosting DLL: these did not reproduce a consistent slowdown. See reports/mcp/local-stdio for all values and limits. The integrated MCP comparison passed 18,000 complete calls; the targeted host suite passed 103 MCP checks and official SDK interoperability passed both revisions. Source-built artifacts remain distinct from published 0.2.1. Full packed-candidate qualification is recorded separately after implementation review.
