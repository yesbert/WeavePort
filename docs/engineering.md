# Engineering guidelines

## Architectural boundaries

WeavePort is an independent backend extension platform integrated through NuGet. HiveWeaver, TreeWeaver and NextPA provide requirements and consumer examples, not mandatory core dependencies. Applications own their domain contracts, workflows and state semantics. WeavePort must support generic plugin execution without embedding agent, place, retrieval or business-process concepts in its core.

Keep dependencies inward: public abstractions do not depend on hosting or runtime adapters; adapters implement contracts without depending on one another; composition belongs to the consumer or host. Add packages only when a real boundary requires them. Stratara and LoomWeaver integrations can be optional adapters, not prerequisites imposed on every consumer.

C#, Python and TypeScript are intended plugin languages. Transport, runtime and deployment choices remain hypotheses until an approved change and evidence support them. NuGet integration does not itself provide a process sandbox. Resource limits, scheduling, credentials, callbacks and failure containment need explicit execution boundaries and tests.

For each proposed execution design, specify the tenant isolation unit and ownership of mutable state. Test tenant A and B running the identical plugin artifact and version while A crashes, hangs or exhausts resources. Separate correctness guarantees from measured latency interference. Test callbacks and shared infrastructure as well as workers. Follow the [current architecture](architecture.md) and its explicit trust boundaries.

## C# conventions

Follow [framework and memory guidance](dotnet-guidance.md) when selecting APIs or optimizing allocations. Record relevant official sources and validate availability against the repository SDK. Treat performance changes as measured hypotheses; preserve security and buffer ownership under cancellation. Read the [worker lifecycle API](worker-lifecycle.md) for implemented retention boundaries; the [current architecture](architecture.md) describes ownership.

Use PascalCase for types and public members, I-prefixed interfaces, camelCase parameters and locals, and `_camelCase` private instance fields. Use Allman braces, nullable reference types, explicit accessibility and async method names ending in Async. Propagate cancellation; avoid synchronous waits on asynchronous work. Use immutable records for data where appropriate and never return null collections.

Prefer guard clauses and cohesive methods. More than seven parameters calls for a meaningful parameter object; a method exceeding 60 code lines or file exceeding 350 warrants simplification or a documented reason, not arbitrary fragmentation. Keep rationale in design notes; use XML documentation on the public package surface, and preserve necessary compiler, tool and licensing directives.

Use source-generated `[LoggerMessage]` methods for C# logging, stable event identifiers and structured fields. Do not copy sibling event-ID ranges. Do not log secrets or plugin payloads by default. Distinguish cooperative cancellation from plugin failure; never hide errors in empty catches.

Use an injected `TimeProvider` for persisted or compared timestamps. Traces use activities and metrics; reproducible benchmark elapsed times use a monotonic clock or a benchmark harness. Tracing alone is not a performance benchmark. Keep simulation time supplied by consumers separate from operational deadlines.

Use centrally configured resilience policies when needed, rather than scattered retry loops. A timeout does not prove that an external side effect failed: retry only under an explicit idempotency policy, preserve uncertain outcomes and make compensation separately observable.

The SDK is selected by global.json. The package-consuming sample contains executable framework-independent checks; changes to testing tools and package dependencies must be deliberate. No sibling framework, assertion-library license or infrastructure service is implicitly required. Public API documentation and package-consumer tests are part of implementing a public contract.

## Documentation

Write maintained artifacts in English. Baseline OpenSpec requirements describe observable, verified behaviour; each requirement has scenarios and supporting implementation/test evidence. Proposed behaviour stays in change deltas until implemented and verified. Public contract names are allowed where necessary; internal types, algorithms and deployment choices belong in design notes.

Document support limits beside the claim. Distinguish intended, tested and supported language/runtime combinations. Avoid transient inventories in agent instructions and avoid duplicate translated specifications. Exact performance targets belong in scoped acceptance scenarios and benchmark protocols, not invented marketing claims.

Keep rationale, alternatives, evidence and consequences in change design notes. When retiring a source, list its original path/identifier in the proposal Impact and preserve its date and rationale. Consumer guides explain use and link to canonical specifications; they do not create competing guarantees.

## Automated readability gate

Run `dotnet run --project tools/WeavePort.CodeStyle -c Release -- "$PWD"` to verify maintained core C#, the three product examples and shared templates. Add `--write` to apply the SDK Roslyn formatter and required braces. The gate rejects drift, methods over 60 code lines and files over 350 lines. `.editorconfig` mirrors the editor conventions. This covers methods and constructors; primary record constructors with many fields are reviewed as meaningful configuration or result objects, not arbitrarily wrapped. Scenario suites outside this scope keep their explicit test setup and assertions together.
