## 1. Baseline and design

- [x] 1.1 Record unchanged-source host/security regressions and native timing evidence before runtime edits.
- [x] 1.2 Specify optional protocol selection, result semantics, lifecycle ownership and supported feature limits.

## 2. Implementation

- [x] 2.1 Add explicit local MCP revision selection, bounded startup and tool exchanges without changing native defaults or adding runtime dependencies.
- [x] 2.2 Preserve shared admission, cancellation, retention, cleanup and authority boundaries; reject incompatible profile and callback settings.

## 3. Verification

- [x] 3.1 Add focused protocol/security and multi-tenant lifecycle regressions including malformed traffic, limits, cancellation and recovery.
- [x] 3.2 Verify both protocol revisions against an exact-version official MCP server and retain dependency license evidence.
- [ ] 3.3 Run packed consumer checks and native before/after benchmarks; measure MCP separately with matched workload boundaries and retain failures.

## 4. Documentation and review

- [x] 4.1 Document configuration, discovery/calls, tool errors, version identity, trust limits and unsupported features with a runnable example.
- [ ] 4.2 Run documentation, public-tree, style, specification and applicable repository verification; synchronize verified requirements.
