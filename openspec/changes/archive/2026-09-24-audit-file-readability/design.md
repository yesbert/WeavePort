## Context

The baseline is main after PR #40. Existing automated checks cover core C#, product examples and author SDK control flow, but exclude much of verification/tooling. A complete audit must not equate a formatter pass with design review.

## Goals / Non-Goals

**Goals:** A reader finds the owning component quickly, recognizes each file's purpose, follows business decisions without reconstructing hidden coupling, and sees why nontrivial lifecycle constraints exist. Every versioned input has a recorded disposition and all concrete findings are resolved and verified.

**Non-Goals:** New product features, changed public contracts, artificial layers/classes, reduced negative-test coverage, rewritten historical evidence, new package versions or releases.

## Decisions

1. Keep a per-file ledger with baseline content identity, category, review status and rationale. Pending files remain pending until actually reviewed. Include new files and record moves so coverage is auditable.
2. Read complete maintained implementations in responsibility groups. Use metrics and searches only to prioritize and detect omissions. Review naming, ownership, dependency direction, failure/cancellation flow, duplication, hidden coupling and command discoverability.
3. Keep guards inside loops, extract cohesive decisions and operations, and preserve locks/finally/resource lifetimes. Prefer clear types and verbs over abbreviations or cosmetic helper fragmentation.
4. Assess executable fixtures and test drivers as maintained code; preserve literal independent protocol expectations and scenario evidence. Historical serialized measurements and binary assets need provenance/format inspection, not formatting that invalidates their identity. Review generated output via its generator and drift checks.
5. Retain public namespaces and serialized shapes. Add directory-level navigation explaining responsibilities and entry points where useful, rather than comments restating individual statements.
6. Verify changed boundaries with focused tests, then the complete frozen package qualification. Keep exact prior evidence separate from the new result.

## Risks / Trade-offs

- Behavioral drift in large refactors → small responsibility-focused edits, preserved public baselines and actual consumer tests.
- File moves break tooling → inspect all references and run repository, build, package and documentation checks.
- Mechanical abstractions obscure the intent → every extraction must name a real decision, state owner or lifecycle step.
- Historical data mistaken for current code → explicitly classify every retained record without changing measured contents.

## Findings and corrections in progress

- The former C# gate used `NormalizeWhitespace`, which collapsed deliberate wrapping and produced awkward tuple/record syntax. Use the actual Roslyn formatting workspace from the pinned SDK, with wrapping/idempotence fixtures. No formatter dependency is added to distributed libraries.
- Worker retention selection mutated counters inside a LINQ predicate. Selection now walks the workers explicitly under the same lock before changing any flags; enumeration order and retention accounting remain unchanged.
- Native/shared clients and host lifetimes now have named partial files for stream delivery, residency, callbacks, maintenance and shutdown. These remain the same state-owning types; no artificial architectural layer is added.
- The Python concurrent runtime now declares its per-invocation state in a dataclass, and protocol test plugins are ordinary fixture files rather than embedded program strings.
- A TypeScript callback observer used a payload property (`callbackError`) as its internal error marker. A new independent wire test reproduced rejection of a successful ordinary object containing that property. Observation now retains only success/failure booleans, separately from returned application data. This restores the existing arbitrary-payload contract; it introduces no new product requirement.

## Reference checks

Checked against SDK 10.0.401, net10.0 and C# 14, with source builds and regression tests:

- [Microsoft C# conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions): clarity, meaningful naming and consistency guide review; metrics are only secondary checks.
- [Roslyn Formatter.Format](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.formatting.formatter.format): format source trivia with a workspace instead of reconstructing whitespace.
- [CancellationTokenSource.CancelAfter](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelafter?view=net-10.0): retain the existing finite timer range when naming its boundary.
- [FileStreamOptions](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestreamoptions?view=net-10.0) and [SHA256.HashSizeInBytes](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashsizeinbytes?view=net-10.0): explicit file access modes and algorithm-defined digest size.
- [PEP 8](https://peps.python.org/pep-0008/) and [Python version specifiers](https://packaging.python.org/en/latest/specifications/version-specifiers/): readable Python organization and unchanged named prerelease ordering.
- [TypeScript narrowing](https://www.typescriptlang.org/docs/handbook/2/narrowing.html): retain runtime validation at untrusted JSON boundaries; separate internal completion state from arbitrary application data.

### Consumer and test review findings

Gateway exchange dispatch and failure translation now have separate named operations. Registry disposal uses an early exit for repeat callers while preserving its shared completion task. Gateway tests keep registration and revocation scenarios in a named file and share per-client metadata assertions without nested loops.

Application samples separate executable verification into `Host/Verification/`. Decision Room separates registration, state reduction, strategy evaluation, configuration and serialization. Document Workshop separates catalogue and runtime resolution from result models and makes malformed page, decline and completion checks individually readable. Appointment Desk names snapshot schema/capacity limits and isolates booking outcome validation. The independent test fixtures retain literal expected protocol values and historic evidence is not rewritten to match new implementations.

Reviewed the current [Microsoft exception guidance](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions) and [PEP 8](https://peps.python.org/pep-0008/) during this pass. Readable messages remain alongside standard exception categories; refactoring does not justify parsing messages or removing cancellation/disposal boundaries.

### Measurement, fixtures and site review

- Extracted benchmark warmup, lane execution, sampled resources and reporting. Density experiments now have named configuration, registration, traffic and report files; observer shutdown remains owned by the run. Kept existing report field names, histogram parameters, traffic order and fixture fault behaviour. No retained measurement was rewritten and build/regression runs are not capacity evidence.
- Split Python density supervision into its process owner, Docker sample owner and shared host/file operations. Added all supervisor source files to provenance. Four deterministic controls cover staircase failure, operator stop, bounded sample timeout and termination of only the owned process group; Docker memory accounting controls remain independent of a daemon.
- Raw protocol fixtures keep literal wire values and intentional faulty operations. Hidden caches, shared defaults, unmanaged resources and malformed frames are negative controls, explicitly distinguished from production patterns. C#, Python and TypeScript raw fixtures passed equivalent native echo/counter/byte-map/workspace/error-isolation transcripts. Docker-only experimental reuse code was read and compiled, not executed on the host.
- Moved the documentation navigation catalogue to `website/navigation.json`. Site preparation, reference generation, discovery metadata and link checks have named phases. Language-example UI state has one owner; CSS formatting and duplicate selector consolidation preserve declarations. Eleven website regressions pass; the TypeScript example test now checks the actual echo contract independently of formatting and unrelated type imports.
- Focused validation so far: concurrent C# wire transcript; benchmark, density, registry, streaming and SDK fixture compilation; density histogram and owned-name controls; two native density negative controls; seven Docker-memory mock controls; four supervisor controls; public-tree negative controls. Frozen all-stage qualification remains pending.

### Hosting and consumer scenario structure

The hosting test entry point now routes to `Protocol/`, `Installations/`, `Lifecycle/`, `Transports/` and `Mcp/` groups, each with named phases rather than nested scenario loops. The native suite passed with the same reported transport (24), envelope (25), endpoint (6), lifecycle (33), JSON-size (215), fragment (4), quarantine (10), admission (8) and diagnostic-code (11) assertion counts; MCP and installation checks also completed. Follow-up directory moves compile cleanly and the scoped readability gate is empty. Multilingual packed SDK consumers are being checked separately after rebuilding their package feed.

All twenty current capability specifications were read against this refactor. Existing opaque-payload, callback-retirement, failure/cleanup, admission, portable pinning and sample recovery requirements remain the behavioral reference; no broader isolation or compatibility guarantee is introduced. Platform guards remain at their existing boundaries, checked against [Microsoft's platform compatibility analyzer guidance](https://learn.microsoft.com/en-us/dotnet/standard/analyzers/platform-compat-analyzer).

## Verification harness review

Native scheduler, optional-package, version, reuse and shared execution harnesses now name their scenarios separately from entry-point setup and fixture ownership. The SDK stream-deadline check explicitly supplies stream deadlines; the gateway unknown-code check expects the documented `unknown-error` fallback. Both fixes correct stale test assumptions rather than changing production policy. Current working-tree evidence includes 382 SDK checks, 88 scheduler assertions plus diagnostic/residency controls, 12 packed version cases, 1,036 native reuse assertions, optional TLS/composition checks, and the shared execution suite with bounded callback abuse and tenant churn. These runs are focused regressions, not the final frozen candidate qualification.

Docker lifecycle/load/security programs have been read and their ownership, phases and resource observations separated. Their builds do not constitute a new Docker or benchmark qualification. Historical measurement bytes remain unchanged. The local capacity staircase now separates bounded configuration, worker growth, timed requests, resource observation and post-measurement analysis; its sampler is cancelled and awaited even when request measurement fails.

Reference review during this stage: [Microsoft test readability and organization guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices) and [Python subprocess ownership and pipe handling](https://docs.python.org/3/library/subprocess.html). Cohesive scenario names and explicit process cleanup apply here; unit-test framework migration is outside this change.

### Installation, supervision and documentation findings

Installation and portable suites now separate discovery, compatibility, approval and launch checks; protocol providers are normal fixture files. Native installation (47 assertions), sample pin/recovery (8), framework selection (35) and portable ownership/startup checks pass. API snapshots retain the public contract; six core and four optional API checks pass against current artifacts. Package checks derive versions from the compatibility inputs instead of embedding stale filenames.

The soak runner had a stale 0.4.0 package filename; it now checks the configured development version. Its k6 consumer incorrectly rejected legal empty stream heartbeats and expected the old generic provider failure status. It now accepts bounded heartbeat batches and checks `sdk-error`, while worker crashes remain `failed`. A five-second native three-language smoke completes with owned cleanup; separate payload and RSS negative controls fail with the expected categories. This is not a multi-hour qualification.

Experimental measurement stages now own state and cleanup explicitly, with separate workload, arrival, observation and lifecycle modules. Parallel starts settle before cleanup. Source inventories cover the extracted helper modules. Four bounded in-memory stage controls exercise saturation, grouped, Poisson and periodic scheduling without Docker. Replaying 230 retained security requests against the original and refactored checks preserves every request and result. No new Docker safety or capacity claim follows from that replay.

Documentation review corrected website navigation instructions after the catalogue extraction and replaced a misleading “current delivered core” benchmark link with the exact historical 0.1.0-internal.2 identity. Examples use braced control flow consistently. Historical reports and license notices retain their original bytes.

### Coverage closure and permanent checks

The final syntax pass covers all maintained C# areas and every tracked/non-ignored Python and TypeScript/JavaScript source outside historical reports and OpenSpec archives. It found and corrected remaining nested orchestration in package/distribution/site checks and one reuse fixture. The candidate now includes all four TypeScript tests, the performance and soak tool controls, release-export controls and website regressions. The gates still cannot certify naming or design quality; the per-file assessment records that separate review.

Archive assessment is intentionally different from maintained-code review: 156 earlier OpenSpec artifacts were checked for role, completed task/scenario structure and unchanged identity; dated reports, logs, nine reproduction scripts and four deliberately defective C# snapshots retain their bytes. The report index explains that `current` in an old directory name is not qualification of today's source. No historical script was executed or rewritten. Third-party notices likewise remain verbatim, without a new legal certification.

The first isolated qualification stopped at the newly added density integration controls because their executable had not yet been built in the fresh checkout. The verifier now builds that harness explicitly before discovering its tests. The test reports a missing prerequisite or launch output directly instead of a secondary missing-result error. The failed candidate is retained separately; local cached builds are not accepted as qualification.
