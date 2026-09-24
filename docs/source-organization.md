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

TypeScript's `runtime/stream-buffer.ts` retains a pending iterator advancement across bounded batches. Runtime code owns its cancellation and cleanup. Python's `stream.py` owns its retained producer. Do not move these lifetimes into registration or recreate invocation context storage per exchange.

Opaque application payloads retain the SDK's generic handler boundary. Changes to wire validation, concurrency, cancellation or buffer ownership require behavioral tests, even when made during a readability refactor.

## Checks and formatting

Run `python3 scripts/check-architecture.py` for dependency direction, export-only SDK facades, centralized versions and logging catalogue coverage. CI runs the same check. Run the C# readability gate described in [engineering guidelines](engineering.md).

Format TypeScript with `npx --yes prettier@3.6.2 --write 'sdks/typescript/src/**/*.ts' '!sdks/typescript/src/protocol.ts'`; use `--check` for verification. Its checked-in configuration applies to editor integrations. Format Python with Black 26.5.1 installed in an isolated development environment: `black sdks/python/weaveport_sdk --exclude protocol.py`. Neither formatter is a runtime dependency or included in published packages. Generated protocol modules are excluded: edit `contracts/protocol.json`, run `python3 scripts/generate-protocol.py`, and verify with `--check` instead of formatting generated output independently.

Central NuGet versions live in the root `Directory.Packages.props`. Preserve `PrivateAssets` and conditional references in each project. Copyable `samples/` and `examples/` explicitly opt out and retain complete consumer versions. Their props files document this boundary. Check lockfile resolved versions after changes; Central Package Management can change lockfile metadata without changing versions. Transitive pinning is disabled.

See [runtime diagnostics](runtime-diagnostics.md) for IDs, error codes and recovery limits; [verification entry points](../tests/README.md) for behavioral checks.
