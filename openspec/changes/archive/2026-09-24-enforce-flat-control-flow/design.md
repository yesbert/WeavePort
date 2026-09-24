## Context

The existing C# gate enforces formatting and size, not the documented guard-clause preference. Runtime logging uses caller-owned ILogger and selected host boundaries; exception construction does not automatically emit a log. Public namespaces, signatures and error/status semantics are already qualified and must remain stable.

## Goals / Non-Goals

Enforce flat business control flow across the existing maintained C# gate scope and Python/TypeScript author SDKs. Preserve cleanup, lock and async ownership. Do not install global first-chance/unhandled exception hooks, log exception messages or turn every argument failure into a platform exception.

## Decisions

- Allow early-exit guards inside loops. Reject nested business conditions and nested loops, including mixed conditional/iteration nesting. Else-if chains are flat alternatives. A guard has no else, contains no further control branch, and ends in return, throw/raise, continue or break.
- Lexically separate functions have their own control-flow scope; do not introduce trivial lambdas/local functions solely to hide nesting. Prefer guard inversion and named operations that express a responsibility. Switch/match dispatch remains available but must not be used to disguise a nested if.
- C# uses the SDK's Roslyn syntax tree; Python uses the standard ast module. TypeScript uses a syntax-aware development check without a runtime dependency. Add fixtures showing both allowed guards and rejected nesting.
- Keep normal diagnostic messages for humans and existing standard exception categories. Document code-driven handling for platform outcomes, explicit logging ownership and the absence of a global logger. Never parse exception messages in new production control flow or log at every throw site.
- Preserve behavior while extracting loop bodies and reducing condition nesting; validate with the existing transport, admission, callback, cancellation, reuse and packed-consumer suites.

## Risks / Trade-offs

- Early exits can move resource release or skip cleanup → keep try/finally, using and locks intact and test their failure paths.
- Method extraction can change evaluation order or captured state → keep mutation order explicit and requalify concurrent paths.
- A syntax metric can encourage superficial rewrites → review responsibilities and names; no generic helper bags or suppression baselines.
- Hardcoded text is not itself an error → retain actionable messages while documenting stable machine codes and logging boundaries.

## Sources reviewed on 2026-09-24

- https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
- https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation
- https://docs.python.org/3/library/ast.html

The repository still resolves SDK 10.0.401, net10.0 and C# 14. No framework/package upgrade is part of this change.

## Implementation notes

The TypeScript checker reuses the exact pinned `typescript` 7.0.2 native AST API (`typescript/unstable/sync` and `typescript/unstable/ast`), verified against its installed declarations and executable fixtures. There is no added dependency or license inventory change. The API is explicitly unstable and upcoming redesign is documented upstream; upgrade it together with the checker, not independently. Source: https://github.com/microsoft/TypeScript/issues/64154. Roslyn syntax was checked against https://source.dot.net/Microsoft.CodeAnalysis.CSharp/Syntax/IfStatementSyntax.cs.html.

Client streams use one bounded pending-item queue to flatten batch/item iteration while retaining per-item limits, cancellation and final cleanup. A queue contains at most the existing 16-item batch bound. Named helpers separate inventory traversal, framework expansion, batching, process termination, routing and sample parsing responsibilities. No global logging hook or changed wire error contract is introduced.

During final review, the extracted C# per-item reader uses `ValueTask` for its common synchronous buffered/completed path and is awaited exactly once; it is never stored or combined. TypeScript takes pending items synchronously before awaiting a new advancement. This avoids adding an unconditional asynchronous wrapper to pending-item delivery. No throughput or allocation improvement over the original implementation is claimed or benchmark-qualified. API ownership source: https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0.
