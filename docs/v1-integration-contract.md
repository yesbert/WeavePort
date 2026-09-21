# V1 integration agreement

**Status: integration contract for the 0.6.0 release, reviewed on 2026-09-18.** This guide defines how our applications should integrate WeavePort. It introduces no new runtime API or released compatibility promise. Existing [baseline specifications](../openspec/specs) define verified behavior; the [current status](status.md) separates remaining work from that behavior. Packages use version `0.6.0`; API evolution remains subject to the exact compatibility matrix.

## Product boundary

WeavePort is an embedded backend plugin platform. Each consuming application owns its coordinator configuration and domain services; installing it does not introduce a mandatory central service. The first release serves owner-controlled applications running owner-controlled plugins. A plugin process can fail independently, but trusted native execution shares the operating-system user and is not a hostile-code sandbox.

The three examples are permanent product assets and integration templates. HiveWeaver, TreeWeaver and NextPA remain requirements sources until separately authorized integration work begins. No common application base class, event store, database, workflow engine or sibling framework is required.

## Responsibility agreement

| Concern | WeavePort supplies today | Consuming application supplies |
|---|---|---|
| Domain contracts | JSON invocation and typed SDK calls/streams | Operation names, request/result schemas, semantic validation and compatibility policy |
| Authority | Immutable `PluginContext`, explicit callback grants, binding checks | Authenticated tenant/profile, permitted plugin selection, callback argument/object validation |
| Execution | Binding, invocation, cancellation, worker restart/disposal, bounded admission | One intentional coordinator budget per application/node deployment; deadlines and overload response |
| Artifacts | Startup version check and configured execution profiles | Approved artifact resolution, stable executable/runtime bytes, installation and activation policy |
| State | Worker-local state for the lifetime of a binding/worker | Durable state, checkpoints, commits, schema migration and recovery |
| Effects | Dispatch status and possible-execution indication | Idempotent request identity, atomic effect/outcome storage or provider reconciliation |
| Data transfer | Bounded frames, SDK calls/streams, optional composition primitives | Resource leases, per-document/operation quotas, staged completion and retention |
| Operations | Lifecycle diagnostics, observable execution results | Correlation with business operations, redaction, shutdown/drain and deployment runbook |

Callback grants authorize names, not every object referenced by their payloads. Use the bound context to look up data and validate each requested object, range or exact action. Changing principal, profile configuration, credentials or grants requires a new binding; mutable request data is not a substitute for that change. See [execution requirements](../openspec/specs/plugin-execution/spec.md) and [worker lifecycle](worker-lifecycle.md).

## Supported surface to carry into V1

| Surface | Intended use | Current source |
|---|---|---|
| `WeavePort.Abstractions` | `PluginContext`, `IPluginSession`, `InvocationResult`, `IHostCallbacks` | [Contracts](../src/WeavePort.Abstractions/Contracts.cs) |
| `WeavePort.Hosting` | Coordinator and selected execution profile | [Lifecycle](worker-lifecycle.md), [native execution](local-execution.md) |
| `WeavePort.Sdk.Client` | Typed local functions/streams over a session | [Client contract](../src/WeavePort.Sdk.Client/IPluginClient.cs), [implementation](../src/WeavePort.Sdk.Client/LocalPluginClient.cs) |
| C#/Python/TypeScript author SDKs | Author functions, streams and host callbacks | [SDK guide](plugin-sdk.md) |
| Optional Composition | Bounded application-controlled large-result composition | [Composition guide](bulk-composition.md) |

The [local compatibility policy](package-compatibility.md) now records the exact core package set and reviewed .NET API baseline. This is an internal review boundary, not a published stability guarantee for every experimental package. Gateway/remote deployment and Docker have separate PoC evidence; the three product reference applications currently qualify native local macOS execution. C#/Python behavior is exercised by Decision Room; Document Workshop and Appointment Desk use C#. TypeScript startup/version checks are separate SDK evidence, not a TypeScript implementation of all three examples. Windows qualification remains open in the existing capacity/platform change.

## Integration sequence

1. **Define the domain boundary.** Create an application-owned contract module with bounded requests/results and explicit terminal states. Decide whether a call computes, stages data or performs an effect. Select the matching reference pattern below.
2. **Resolve an approved installation.** Choose a concrete plugin release, entry point and runtime from trusted deployment configuration. Preserve that selection for the operation's recovery lifetime. Do not rebuild files underneath a live binding. A version label alone does not authenticate file contents.
3. **Create the coordinator budget.** Reuse one `PluginHost` for the intended budget boundary. Independent hosts have independent counters; a host per incoming request does not impose aggregate node limits. Appointment Desk uses the [shared coordinator template](embedded-coordinator.md) for operation admission and owned shutdown. Example budgets are not a service-host sizing prescription.
4. **Bind the operation authority.** Construct `PluginContext` from authenticated application state, add only the needed callback names and choose the execution profile with explicit required protections. Bind with application-owned callbacks. Exclusive bindings serialize work within the common budget. Shared installations permit approved concurrent unary calls with per-invocation tenant authority; use one `PluginApproval` and `ShareAsync` for that mode.
5. **Execute and validate.** Pass cancellation and deadlines. Validate domain output before committing application state. For large results use bounded transfer steps and leases; do not infer that transport streaming makes a whole operation transactionally complete.
6. **Recover by effect semantics.** Preserve the selected release, authoritative inputs and the operation's commit boundary. Apply the pattern below. Version, schema or identity mismatch must be surfaced rather than silently switching to a different release or restarting a different action.
7. **Release owned resources.** Dispose the client/binding and application leases when the logical operation ends. Dispose the coordinator after its owned operations settle. Observe cleanup uncertainty rather than assuming a disposed client proves every external effect or descendant process stopped.

For executable integration code, start with [Decision Room](../samples/DecisionRoom/README.md), [Document Workshop](../samples/DocumentWorkshop/README.md) or [Appointment Desk](../samples/AppointmentDesk/README.md). These use packaged WeavePort libraries and only reference their own domain contracts directly.

## Recovery patterns proven by the examples

| Pattern | Commit boundary | Recovery rule | Demonstrated evidence |
|---|---|---|---|
| Calculation — Decision Room | Host-validated evaluation and transition appended to the journal | Replay committed events into a fresh worker; recompute only uncommitted deterministic work; retain pinned release | [Guide and verification entry point](../samples/DecisionRoom/README.md) |
| Import — Document Workshop | Validated complete extraction moved from staging into the document store | Discard handled incomplete attempts; restart extraction from captured/approved input as a new import attempt | [41 assertions (historical) — pre-public record](history.md) |
| Action — Appointment Desk | Approved command persisted before dispatch; booking and outcome saved together | Reconcile/repeat the exact scoped command; return the original terminal outcome | [30 assertions and separate-process recovery (historical) — pre-public record](history.md) |

These are distinct application semantics. Document Workshop does not resume a partially decoded document after coordinator failure. Decision Room replay does not make external actions exactly-once. Appointment Desk proves a local single-coordinator transaction; a remote provider needs its own idempotency/reconciliation mechanism.

## Failure interpretation

`InvocationResult.Status` describes runtime execution, not domain success. An `ok` response still requires semantic validation. Domain refusal, conflict and unavailable results belong in the application contract.

`InvocationResult.MayHaveExecuted` and `PluginCallException.MayHaveExecuted` express dispatch uncertainty. `false` means that attempted dispatch did not execute; it says nothing about earlier attempts under the same application request. `true` requires effect-aware reconciliation. The typed local client turns runtime cancellation into `OperationCanceledException`, which does not carry that flag. Conservatively retain action identity on cancellation rather than assuming no effect occurred. Appointment Desk demonstrates this behavior.

A `busy` result is an admission outcome, not permission for unbounded retries. The application must bound queueing, retry time and concurrency. A worker replacement can reconstruct execution capacity but cannot reconstruct application state by itself. Callback completion can outlive caller cancellation; idempotency and durable results remain application/provider responsibilities.

## Versions and installation gaps

Keep four identities distinct: host/SDK package version, plugin artifact release, domain contract/schema version and transport protocol version. The required installation compatibility declaration now checks host API/protocol and package/SDK identities separately. Domain contracts still require exact equality rather than range negotiation; content pins and the startup release check remain independent guards.

All three examples now consume the [shared installed-plugin resolver](installed-plugins.md), with manifest/content validation and retained release identity. Decision Room and Appointment Desk re-resolve persisted pins; Document Workshop records a pin per complete import. This requires stable deployment bytes and trusted metadata; it is not an enforcing immutable filesystem or a general package manager. Current support limits and outstanding qualification are tracked in the [current status](status.md).

## Consumer acceptance evidence

Before an application integration is accepted, its own domain tests should show: a successful packaged call; alternate implementation where relevant; denied/foreign callback authority; worker loss at the commit boundary; cancellation with honest effect status; two scopes active while one fails; and preserved release/schema identity on recovery. Add capacity and cleanup tests for the application's actual operating envelope. Passing an example does not qualify a different deployment automatically.

Historical example assertions are development evidence, not a new combined release run: Decision Room 33, Document Workshop 41, Appointment Desk 30, plus 12 multilingual SDK version checks. The [qualified internal candidate (historical) — pre-public record](history.md) now records a combined 234-assertion run against one fixed package/artifact set.

The [native operations runbook](native-operations.md) qualifies guarded manual recovery for Appointment Desk. Other integrations must adopt equivalent run ownership and restart gates; automatic descendant cleanup and power-loss durability are not implied.
