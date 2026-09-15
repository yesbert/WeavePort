## Why

Applications need to reuse ordinary functions exposed by MCP servers while retaining WeavePort's native plugins and shared worker lifecycle. Requiring every plugin to adopt MCP would add protocol costs and remove useful native semantics without benefiting generic application extensions.

## What Changes

- Add explicit optional local stdio MCP tool discovery and invocation alongside the unchanged default native protocol.
- Keep process launches, tenant admission, deadlines, worker retention and cleanup under the existing host.
- Bound and validate MCP traffic; do not grant MCP servers implicit host callbacks or transmit application configuration as protocol metadata.
- Document supported protocol revisions, result semantics, trust limits and incompatibilities with native author/client SDK operations.
- Record native before/after benchmarks, MCP interoperability and focused security regressions.
- Non-goals: exposing WeavePort as an external MCP gateway; remote HTTP MCP; AI/model integration; arbitrary MCP extensions; OS sandbox improvements; package publication.

## Capabilities

### New Capabilities

- `mcp-plugins`: Optional bounded local MCP tools integration with explicit compatibility and lifecycle ownership.

### Modified Capabilities

None. Existing native contracts remain supported unchanged.

## Impact

Hosting profile selection, process startup and session dispatch; MCP protocol implementation and dedicated test fixtures; consumer documentation and performance evidence. No migration of existing plugins or default protocol change. Production dependency selection must preserve the lightweight native path; test SDK dependencies are isolated from distributed packages.
