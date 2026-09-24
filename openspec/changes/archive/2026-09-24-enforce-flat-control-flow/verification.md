# Verification evidence

## Final isolated candidate

Snapshot `5fcf52fe872e3405b8c8dd12d96ba49955828ae4` passed the complete native macOS candidate workflow on 2026-09-24: **3555 recorded assertions, 428 frozen artifact files, 64 stages**. Evidence remains at `artifacts/candidates/20260924-084521-533cee45/result.json`, with stage logs and the artifact manifest alongside it.

A temporary Git index produced the snapshot without changing the owner's branch or staging index. Qualification used a detached clean checkout and fresh dependency caches. A content-hash comparison found no working-tree differences from the final snapshot before recording this evidence. Only task/verification records and change archival follow qualification; runtime code, build configuration, tests and tooling remain identical.

The earlier snapshot `61dfe58bd4d62294bae0da33c0d2dc2c081af568` also passed the full workflow at `artifacts/candidates/20260924-083947-65399d4d`. Final review then avoided unconditional asynchronous wrapping of buffered stream items; the final snapshot above requalified those changes in full. An earlier administrative invocation with unsupported `--help` stopped at checkout and is not qualification evidence.

## Coverage

- C# Roslyn, Python ast and TypeScript compiler AST checks: zero forbidden nesting in their documented maintained scopes, with positive and negative fixtures. C# formatting and size gates also passed.
- Five architecture guard tests, 19 Python SDK tests and two Node context/cleanup tests passed. TypeScript built with the existing pinned compiler; no dependency was added.
- Source and packed host, scheduler, reuse, shared execution and concurrent SDK regressions passed.
- All-language stream deadlines, available-item delivery before callbacks, residency, paused cancellation, late disposal and queued cancellation passed for source and packed consumers.
- MCP interoperability, portable installations, all three application templates, optional Gateway/Composition, recovery and exact public API/package compatibility passed.
- The final negative package control refused a modified package; the 428 frozen consumer artifacts remained unchanged throughout verification.

The assertion total follows the existing candidate harness's PASS-line counting convention; syntax fixtures and Python/Node test counts are also recorded separately in their stage logs.

## Boundaries

This is functional and package qualification on native macOS. It adds no measured performance claim, new OS qualification or Docker isolation guarantee. Docker was not stopped or reconfigured. The control-flow gates cover maintained C# core/templates and the Python/TypeScript author SDKs, not every test fixture or unrelated tool in the repository. Standard exception categories/messages remain; no global logging hook or wire/public contract change was introduced by this follow-up. No package was published.
