# Engineering guidelines

## Architectural boundaries

WeavePort is an independent backend extension platform integrated through NuGet. HiveWeaver, TreeWeaver and NextPA provide requirements and consumer examples, not mandatory core dependencies. Applications own their domain contracts, workflows and state semantics. WeavePort must support generic plugin execution without embedding agent, place, retrieval or business-process concepts in its core.

Keep dependencies inward: public abstractions do not depend on hosting or runtime adapters; adapters implement contracts without depending on one another; composition belongs to the consumer or host. Add packages only when a real boundary requires them. Stratara and LoomWeaver integrations can be optional adapters, not prerequisites imposed on every consumer.

C#, Python and TypeScript author SDKs are implemented and qualified within the documented platform matrix. New transport, runtime and deployment choices remain hypotheses until an approved change and evidence support them. NuGet integration does not itself provide a process sandbox. Resource limits, scheduling, credentials, callbacks and failure containment need explicit execution boundaries and tests.

For each proposed execution design, specify the tenant isolation unit and ownership of mutable state. Test tenant A and B running the identical plugin artifact and version while A crashes, hangs or exhausts resources. Separate correctness guarantees from measured latency interference. Test callbacks and shared infrastructure as well as workers. Follow the [current architecture](architecture.md) and its explicit trust boundaries.

## C# conventions

Follow [framework and memory guidance](dotnet-guidance.md) when selecting APIs or optimizing allocations. Record relevant official sources and validate availability against the repository SDK. Treat performance changes as measured hypotheses; preserve security and buffer ownership under cancellation. Read the [worker lifecycle API](worker-lifecycle.md) for implemented retention boundaries; the [current architecture](architecture.md) describes ownership.

Use PascalCase for types and public members, I-prefixed interfaces, camelCase parameters and locals, and `_camelCase` private instance fields. Use Allman braces, nullable reference types, explicit accessibility and async method names ending in Async. Propagate cancellation; avoid synchronous waits on asynchronous work. Use immutable records for data where appropriate and never return null collections.

Prefer guard clauses and cohesive methods. More than seven parameters calls for a meaningful parameter object; a method exceeding 60 code lines or file exceeding 350 warrants simplification or a documented reason, not arbitrary fragmentation. Keep rationale in design notes; use XML documentation on the public package surface, and preserve necessary compiler, tool and licensing directives.

Use source-generated `[LoggerMessage]` methods for C# logging, stable event identifiers and structured fields. Do not copy sibling event-ID ranges. Do not log secrets or plugin payloads by default. Distinguish cooperative cancellation from plugin failure; never hide errors in empty catches.

Use an injected `TimeProvider` for persisted or compared timestamps. Traces use activities and metrics; reproducible benchmark elapsed times use a monotonic clock or a benchmark harness. Tracing alone is not a performance benchmark. Keep simulation time supplied by consumers separate from operational deadlines.

Keep repeated wire method names, reserved operation identifiers and transport metadata keys in small internal constants grouped by protocol. Share SDK operation identifiers as linked source when the SDK and client must remain independently packaged. Keep wire fixtures independent so tests can detect accidental value changes. Do not extract ordinary diagnostics or formatting literals solely to remove strings, and do not add public constants without a consumer API need. C# constants are substituted at compile time; see [Microsoft’s constants guidance](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/constants).

Use centrally configured resilience policies when needed, rather than scattered retry loops. A timeout does not prove that an external side effect failed: retry only under an explicit idempotency policy, preserve uncertain outcomes and make compensation separately observable.

The SDK is selected by global.json. The package-consuming sample contains executable framework-independent checks; changes to testing tools and package dependencies must be deliberate. No sibling framework, assertion-library license or infrastructure service is implicitly required. Public API documentation and package-consumer tests are part of implementing a public contract.

## Documentation

Write maintained artifacts in English. Baseline OpenSpec requirements describe observable, verified behaviour; each requirement has scenarios and supporting implementation/test evidence. Proposed behaviour stays in change deltas until implemented and verified. Public contract names are allowed where necessary; internal types, algorithms and deployment choices belong in design notes.

Document support limits beside the claim. Distinguish intended, tested and supported language/runtime combinations. Avoid transient inventories in agent instructions and avoid duplicate translated specifications. Exact performance targets belong in scoped acceptance scenarios and benchmark protocols, not invented marketing claims.

Keep rationale, alternatives, evidence and consequences in change design notes. When retiring a source, list its original path/identifier in the proposal Impact and preserve its date and rationale. Consumer guides explain use and link to canonical specifications; they do not create competing guarantees.

## Automated readability gate

Run `dotnet run --project tools/WeavePort.CodeStyle -c Release -- "$PWD"` to verify maintained core C#, the three product examples and shared templates. Add `--write` to apply the SDK Roslyn formatter and required braces. The gate rejects drift, methods over 60 code lines and files over 350 lines. `.editorconfig` mirrors the editor conventions. This covers methods and constructors; primary record constructors with many fields are reviewed as meaningful configuration or result objects, not arbitrarily wrapped. Scenario suites outside this scope keep their explicit test setup and assertions together.

## Source ownership and diagnostic vocabulary

Follow the [source organization guide](source-organization.md) when adding a feature or type. Preserve inward package dependencies, keep public contract types in named files and keep SDK facade modules free of runtime implementation. Run `python3 scripts/check-architecture.py` alongside the readability gate. Centralize maintained NuGet versions in `Directory.Packages.props`; standalone consumer templates retain their explicit versions.

Platform-specific exceptions expose documented stable codes while standard argument, cancellation and transport exceptions retain their normal categories. Preserve execution-uncertainty metadata. Never infer automatic retry safety from a code. Add generated logging IDs to the named catalogue without renumbering existing values; see [diagnostic contracts](runtime-diagnostics.md).

## Flat control flow and clean-code review

Use early returns for preconditions, unsupported cases and completed paths. Do not nest business `if` branches, loops, or a loop inside a branch. Flat `else if` / `elif` alternatives are allowed. Inside a loop, an `if` guard may perform necessary work and then exit with `continue`, `break`, `return`, `yield break`, or `throw` / `raise`; it must have no `else` or nested decision/iteration. `yield return` is not an early exit. Keep `try/finally`, cancellation, locks and disposal boundaries intact when flattening flow.

Extract named operations with a single responsibility when a loop body requires its own decisions or iteration. Do not hide nesting in trivial lambdas, local functions, ternaries, boolean expressions or switch/match blocks solely to satisfy a metric. Keep mutation and evaluation order visible, choose domain-specific names, avoid unrelated helper collections, and keep each statement focused on one action. Prefer a cohesive class when several operations share owned state; facade modules export the supported surface rather than hosting runtime classes.

The C# readability gate now also rejects forbidden control flow. Run `python3 scripts/check-python-control-flow.py` for the Python SDK and `npm run check:control-flow --prefix sdks/typescript` after `npm ci --prefix sdks/typescript` for the TypeScript SDK. All three checks execute positive and negative syntax fixtures before scanning maintained sources. The isolated candidate workflow runs them in CI. TypeScript uses the AST API of the existing pinned compiler as development tooling; its unstable API must be rechecked deliberately when upgrading TypeScript. No parser is shipped with the SDK.

The C# gate covers `src` and the three maintained application examples plus shared templates; the Python and TypeScript gates cover their author SDK implementation directories. Test scenarios and unrelated tooling are not implicitly claimed to be checked by these gates. Review still owns naming, cohesion, duplication, dependency direction and meaningful method boundaries; a passing syntax metric is not proof of every clean-code property.

Exception messages explain failures to humans. Use existing standard exceptions for invalid arguments, invalid state, cancellation and transport failures. Use the documented stable code/metadata for platform outcomes, never message parsing. Do not replace useful diagnostics with numeric-only messages or introduce a custom exception per throw site. Log at the boundary that owns recovery or propagation, not in every helper; see [logging ownership](runtime-diagnostics.md#logging-ownership-and-exception-messages).

## Meaningful literals and protocol definitions

Maintain shared wire operations, field names, frame kinds, codes and byte/count limits in `contracts/protocol.json`. Run `python3 scripts/generate-protocol.py` after an intentional contract change and `--check` in verification. Generated internal C#, Python and TypeScript definitions retain existing values; independent fixtures must continue asserting literal wire expectations. Generation does not grant permission to change protocol values or limits.

Name non-obvious limits with their unit and purpose, near the responsible component. Keep tunable operating policy in validated options, fixed interoperability limits in the protocol contract, and unrelated equal values separate. Use typed TypeScript frames with runtime validation at JSON boundaries. Do not replace simple zero/empty values, human-readable exception messages or self-explanatory option defaults with indirection. Avoid a global `Constants` collection and public C# compile-time constants without a consumer contract need.

Preserve primary execution failure and every registered author-resource cleanup cause, and retain host worker startup/invocation failures when worker teardown also fails. Do not infer a universal multi-error guarantee for arbitrary consumer `finally` blocks or iterators. Standard exception categories and readable messages remain useful locally; remote codes are stable vocabulary, never exception-message parsing. Cancellation must be attributed to a requested token or expired owned deadline, not inferred merely from exception type. Raw exception diagnostics require an explicit caller-owned protected destination.
