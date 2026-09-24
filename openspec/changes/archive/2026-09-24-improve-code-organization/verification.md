# Verification evidence

## Isolated candidate

The implementation snapshot `f5c6e2cb528eee086728cc917c650a5ac3f00104` passed the repository's complete native macOS candidate workflow on 2026-09-24: **3555 recorded assertions, 428 frozen artifact files, 62 stages**.

The snapshot was created through a temporary Git index and qualified in a detached clean checkout with fresh dependency caches. The owner's branch and staging index were not modified. Evidence is retained locally at `artifacts/candidates/20260924-081547-8978211b/result.json`, with stage logs and the immutable artifact manifest alongside it.

Coverage includes source and packed host/scheduler regressions, real MCP interoperability, portable runtime selection, all three application templates, source and packed reuse/shared/concurrent consumers, stream/source lifecycles, author SDK packaging/version checks, optional Gateway/Composition, recovery, public API review and refusal of changed packages. Each source/packed reuse run passed 1,036 assertions. SDK context/protocol tests passed 19 Python/unittest cases and two Node tests; the candidate assertion total uses the existing harness's PASS-line counting convention.

After this snapshot, only verification/task records, the verified diagnostic spec merge and generated documentation changed. No runtime, package configuration, test or tooling implementation changed. Final documentation/specification checks were run on the working tree.

## Focused checks

- Full solution Release build: zero warnings and errors.
- C# readability gate: no formatting or size violations.
- Prettier 3.6.2 and Black 26.5.1 checks: passed.
- Architecture guard tests: five passing positive/negative tests.
- Release tooling: 12 tests passed.
- Public API diff: only ErrorCode properties/getters on PluginCallException and PluginVersionMismatchException; prior signatures retained.
- Existing resolved NuGet package versions: unchanged; CPM metadata and local candidate content hashes can change.
- Strict OpenSpec validation, generated documentation, documentation links, public-tree and whitespace checks: passed.

## Observed failure and correction

The initial candidate stopped at portable Node execution because test bundle builders assumed a flat JavaScript output directory. Portable/shared builders now preserve nested paths and the mixed-runtime inventory includes all nested modules. The second full candidate passed with these corrections. The failed run remains at `artifacts/candidates/20260924-081314-161fa8cb` and is not counted as successful qualification.

## Limits

This is native macOS functional and package qualification. It adds no performance claim, Windows/Linux runtime qualification or Docker isolation guarantee. Docker was not reconfigured or stopped. Standard argument/cancellation/transport exceptions retain their standard semantics; only the documented platform exceptions gain codes. No packages were published.
