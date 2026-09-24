# Source organization

Start with the package that owns the behavior, then the feature folder. Public C# namespaces remain stable even where files move into feature folders. Folder organization improves navigation; project references enforce the package boundary.

## Dependency direction

- `Abstractions` owns host context, results and session/callback contracts and has no project dependencies.
- `Sdk` owns author registration and execution; it does not reference Hosting.
- `Sdk.Client` depends on Abstractions and adapts sessions into application-facing calls, streams and sources.
- `Hosting` implements session execution and installation binding over Abstractions and Sdk.Client.
- `Composition` and `Sdk.Gateway.Client` build on Sdk.Client. The gateway server uses its protocol/client package and owns registration and credential resolution.
- `Testing` depends only on Abstractions.

This is the inward dependency rule applied to a library platform. The consumer's business domain remains outside WeavePort. Add a new package only for an independently useful contract or deployment boundary. Do not introduce empty Domain/Application layers or a mediator just to resemble an application template.

## C# navigation

| Location within Hosting | Ownership |
| --- | --- |
| `Hosting/` | Public host composition and registration |
| `Installations/` | Catalogue, approval, sealing, verified artifact selection |
| `Installations/Runtimes/` | Ecosystem requirement parsing and runtime probes |
| `Scheduling/` | Fair queues, dispatch, completion and scheduling policy |
| `Sessions/` | Bound invocation authority, exchange and admission |
| `Workers/` | Worker acquisition, reuse, startup and residency |
| `Adapters/Native/`, `Adapters/Docker/` | Execution-specific launch, IO and cleanup |
| `Protocol/Native/`, `Protocol/Mcp/` | Envelope and protocol interpretation |
| `Diagnostics/` | Safe structured logging, stable IDs and diagnostic exceptions |

Keep one named public contract per file. Related partial implementations stay together within their owning feature; partial files are not separate architectural layers. Put a new installation operation beside installation code, including its validation, rather than growing generic Helpers/Managers directories. Split collaborators when they own different state or lifetimes, not simply to reduce a line count.

The author SDK keeps registration at its root, session resource ownership in `Sessions/`, and channel/execution code in `Runtime/`. Client implementations live in `Local/` and `Remote/`. Gateway registration, operations and protocol metadata have separate folders.

## TypeScript and Python

`index.ts` and `weaveport_sdk/__init__.py` only expose the public API. Registration lives in `application`; the runtime owns transport and dispatch. Session/context modules own callback access and reverse-order cleanup. Source and stream modules own bounded output. Internal modules import each other directly, never through the public facade.

TypeScript's `runtime/stream-buffer.ts` retains a pending iterator advancement across bounded batches. Runtime code owns its cancellation and cleanup. Python's `stream.py` owns its retained producer. TypeScript's `runtime/channel.ts` owns the line transport; Python's `concurrent_call.py` gives each concurrent invocation explicit state. Do not move these lifetimes into registration or recreate invocation context storage per exchange.

Opaque application payloads retain the SDK's generic handler boundary. Changes to wire validation, concurrency, cancellation or buffer ownership require behavioral tests, even when made during a readability refactor.

## Examples and verification scenarios

The three application samples keep executable verification scenarios under `Host/Verification/`, separate from application orchestration and persistence. Start at `Host/Program.cs`, follow the named workflow (`RoomRunner`, `Importer`, or `Desk`), then inspect its owned journal, source lease, or calendar store. Sample worker registration should reveal the operations immediately; Decision Room keeps state reduction in `RoomOperations` and scoring in `Strategy`.

SDK wire tests use real programs in `sdks/python/tests/fixtures/`, launched by `worker_harness.py`. The scenarios compare independent literal protocol expectations against both author SDKs. Fixture failures and deliberate protocol violations are test inputs, not patterns for production code.

Hosting verification is grouped by boundary under `tests/WeavePort.Hosting.Tests/`: `Protocol`, `Installations`, `Lifecycle`, `Transports` and `Mcp`. Its [local guide](../tests/WeavePort.Hosting.Tests/README.md) maps these groups to their entry points. SDK consumer checks separate complete calls, streams and cross-tenant isolation; `Program.cs` only selects the suite.

Benchmark orchestration keeps registration, warmup, measurement and reporting visible. `benchmarks/WeavePort.Density/DensityRun.*.cs` groups those phases around one owned experiment; `tools/performance/density_stage.py` owns process supervision and asynchronous memory observation. Historical `reports/` data preserves its original source identity and is never reformatted as current evidence.

The website navigation catalogue lives in `website/navigation.json`; `scripts/build-website.py` stages canonical pages, generates references and exports AI retrieval files. Website source documents remain the authoritative prose; generated site output stays under `artifacts/`.

## Checks and formatting

Run `python3 scripts/check-architecture.py` for dependency direction, export-only SDK facades, centralized versions and logging catalogue coverage. CI runs the same check. Run the C# readability gate described in [engineering guidelines](engineering.md).

Format TypeScript with `npx --yes prettier@3.6.2 --write 'sdks/typescript/src/**/*.ts' '!sdks/typescript/src/protocol.ts'`; use `--check` for verification. Its checked-in configuration applies to editor integrations. Format Python with Black 26.5.1 installed in an isolated development environment: `black sdks/python/weaveport_sdk --exclude protocol.py`. Neither formatter is a runtime dependency or included in published packages. Generated protocol modules are excluded: edit `contracts/protocol.json`, run `python3 scripts/generate-protocol.py`, and verify with `--check` instead of formatting generated output independently.

Central NuGet versions live in the root `Directory.Packages.props`. Preserve `PrivateAssets` and conditional references in each project. Copyable `samples/` and `examples/` explicitly opt out and retain complete consumer versions. Their props files document this boundary. Check lockfile resolved versions after changes; Central Package Management can change lockfile metadata without changing versions. Transitive pinning is disabled.

See [runtime diagnostics](runtime-diagnostics.md) for IDs, error codes and recovery limits; [verification entry points](../tests/README.md) for behavioral checks.

### Verification and measurement harnesses

Large test executables keep `Program.cs` as the command entry point. In the local consumer, `Verification/` owns prerequisite and behavior checks, while `Capacity/` owns bounded configuration, worker growth, request stages and resource observation. The Docker capacity harness separates `Execution/`, `Measurement/`, `Resources/` and `Reporting/`; reports consume retained observations rather than performing new measurements. Optional-package checks live under `Composition/`, `Gateway/` and `Verification/`. Shared execution keeps `Scenarios/` separate from its host-side `HostFixtures/` and language worker scripts in `fixtures/`.

These directories identify responsibilities, not separate production packages. Scenario files keep setup, assertions and cleanup together where they explain one behavior. Fault injection, foreign-tenant attempts, malformed frames and uncooperative cleanup in test providers are intentional negative controls and must remain distinguishable from production examples.

Installation and portable checks keep launcher fixtures as ordinary source files under `tests/installations/fixtures/`; scenario classes describe discovery, policy, sealing and runtime selection. Package/API checks are separate from runtime scenarios so a metadata mismatch can be investigated without following a worker lifecycle.

Python measurement entry points (`reuse.py`, `reuse_matrix.py`, `density.py`, `tools/soak/run.py`) select an experiment and delegate its lifetime. `MatrixMeasurement` owns one stage, with `matrix_workloads`, `matrix_execution`, `matrix_observation` and `matrix_lifecycle` separating requests, arrivals, reporting and cleanup. `SoakRun` owns the supervised native process group; `configuration.py` validates its limits and `support.py` supplies bounded process and evidence operations. The k6 workload separates transport (`session.js`), protocol encoding (`protocol.js`) and checked operations (`operations.js`). New helpers belong to the recorded source-hash set as well as the import graph.
