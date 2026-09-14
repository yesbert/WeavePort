# Design

Follow the current llms.txt proposal (https://llmstxt.org/, reviewed 2026-09-14): structured index, Markdown page alternatives and discovery relations. llms-full.txt is a companion convention. Repository copies retain source links; deployed copies resolve curated guides, specs and API signatures within one website artifact to avoid unpublished source links and revision drift. CI rejects stale tracked copies; builds generate website resources and deployments replace the complete artifact. No independent server polling or credentials are introduced.

GitHub configuration follows https://github.com/github/github-mcp-server and its docs/remote-server.md: read-only repository endpoint with client-owned authentication. This is repository context, not plugin execution. Reject an invented WeavePort MCP URL and universal client configuration. No authenticated MCP session is claimed.

## Verification

Nine regression tests passed: index format, source freshness, website-local retrieval, HTML discovery, unchanged API signatures and existing link/example checks. Build completed with no warnings: 39 HTML pages, 616 local references, 53 Markdown resources. All 43 public HTTPS resources in the index/entry check matched local bytes, including every index target. Maintained documentation links, generated-file freshness, public-tree checks and strict OpenSpec validation passed. GitHub release-tag status retrieval returned HTTP 200; no authenticated MCP client test was run.
