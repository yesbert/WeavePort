## Context

See proposal.md. Baseline is 45ef50a (0.5.0). Existing session reuse is sequential and cooperative. Scheduling wraps a private host, while streams require direct sessions. All author runtimes read callback replies inline. The runtime is .NET SDK 10.0.401, net10.0, C# 14; no preview features or new runtime dependencies are planned.

## Goals / Non-Goals

A caller selects one host, one verified installation and one operator approval. Domain naming, activation lists and durable workflow state remain application-owned. Shared unary workers serve concurrent tenants; shared streams and remote MCP sharing are excluded. Existing exclusive streams gain residency and live delivery.

## Decisions

1. **One host and budget.** Fold the scheduler into internal host machinery. Queue timeout zero means fail-fast. Preserve normal/heavy classes, bounded registrations/bytes, pristine targets and measured timing. Customer-bound retention is explicit: default state affinity, opt-in reconstructible eviction. Do not infer evictability from ownership. Nested calls from callback scopes are denied consistently to prevent bounded-pool dependency deadlocks; applications orchestrate fan-out outside callbacks. Fair selection uses active calls per tenant, including shared invocations, with least-recently-served ties.
2. **Three ownership values.** CustomerBound remains default; ApprovedSessions remains sequential and requires cleanup; Shared requires manifest support, explicit operator allowance and ShareAsync. Shared workers charge only the host budget and are resident until disposal or failure. Retain a single ownership setting instead of a second sharing flag on host profiles. Initially support shared native ProcessProfile only; Docker and MCP sharing are refused explicitly.
3. **Concurrent protocol revision 2.** Shared SDKs advertise protocol 2 and concurrentCalls revision 1. Existing hosts reject protocol 2. The host sends a configure frame with degree before invocation dispatch. Legacy exclusive execution stays protocol 1. One reader routes invocation and callback identities; one serialized writer prevents frame interleaving. SDK execution contexts and cleanup are per invocation, never worker-global. Python sync handlers use a bounded thread pool, async handlers interleave; TypeScript CPU work remains author's worker-thread responsibility.
4. **Failure and cancellation.** Author/callback failures, denied callbacks and callback limits fail one invocation. Malformed frames, unknown IDs, transport loss and bounded silence retire the channel. Cancellation returns promptly to the caller but retains the slot and immutable context until a terminal acknowledgement or channel retirement. Revoke callbacks immediately on cancellation; ignore no unknown identities. Unacknowledged cancelled slots are bounded by policy, then retire. No automatic replay of in-flight calls; MayHaveExecuted stays true. Restart resident workers with bounded startup time and a sliding restart budget; exhausted budget exposes disabled state.
5. **Catalog and approval.** Preserve exact digest pinning and existing release selectors. A multi-plugin root lists selected verified releases per contract. Declared runtime aliases form a subset of approved paths, while every selected runtime hash remains verified. Manifest launch metadata declares memory and supported ownership/degree. Approval bounds memory, concurrency, timeouts and callbacks. Installation identity supplies plugin/version/profile identity once. Client abstractions must remain dependency-inward; Hosting must not depend cyclically on client implementation.
6. **Streams.** Hold an operation-wide residency lease across start/next/close, including consumer pauses. Exactly two stream deadlines: per exchange and total; unary deadlines stay separate. Flush available items without waiting to fill a batch; empty unfinished batches are legal heartbeats. Retain one outstanding iterator advancement between heartbeat exchanges rather than cancelling and recreating enumeration. Shared workers reject streams before dispatch.
7. **Large plugin sources.** Extend existing Composition and result scopes with CollectAsync over bound clients, bounded base64 blocks and source open/read/close semantics. Keep JSON stream's 64 MiB limit. SDK author helpers own source lifetime and bounded reads; consumers receive only committed ResultHandle objects. Cancellation/failure closes source state or retires the worker and removes partial files.
8. **Diagnostics.** Safe structured events remain default. Raw stderr forwarding is explicit opt-in with 4096-character lines and one 128-line dropping delivery queue per host; a blocked caller logger can leave one background delivery task alive after disposal but cannot block worker pipe draining or shutdown; it can contain tenant data even without tenant configuration. Never infer safety from Shared ownership. Report resident readiness, restart/disabled state and slot usage.
9. **Qualification.** Functional barriers prove actual overlapping invocations, identity, cancellation isolation, callback revocation and retained slots independently of CPU speed. Performance measurements compare degree one and sixteen on the same machine, recording environment, tails, throughput and memory. A fixed 1.4x factor is a measured reference, not a hardware-independent release gate. Optimize only after identifying a measured cost; preserve before/after evidence.

## Risks / Trade-offs

Shared memory permits accidental cross-tenant data retention and process crashes affect all in-flight tenants; explicit trusted-code approval documents this accepted boundary. Native threads cannot be forcibly cancelled safely; retained slots and bounded channel retirement prevent false capacity release. Resident memory reduces capacity available for connectors; reject startup when reservations cannot fit. Unified scheduling changes nested-call and retention choices; migration must explain each breaking behavior.

## Migration Plan

Publish a new pre-1.0 minor version after complete source/packed qualification, audit and benchmark evidence. Update all seven NuGet packages and exact compatibility inputs together, package matching author SDK assets, regenerate LLM documentation and migrate every maintained sample. Use green CI and the gated release workflow; do not change deployment protections or Docker services. Rollback uses the previous version and matching declarations, never a moved release tag.

## Technical references

- https://learn.microsoft.com/en-us/dotnet/core/extensions/channels
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task-1.waitasync?view=net-10.0
- https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/cancel-non-cancelable-async-operations

Cancellation of a wait is distinct from completion of the underlying operation. Buffers, slots and callback admission remain owned until actual completion.
