## Context and decisions

The existing `PluginHost` owns immutable bindings and bounded adapter reservations; used workers never return to the pristine pool. The new optional `ScheduledPluginHost` owns a private direct host with per-tenant ceilings equal to the global budget. It never exposes the private host for bypass. Legacy consumers keep their existing single-flight/fail-fast/state-affine semantics.

One immutable registration is allowed per `(tenant, plugin)`; versions, grants and configuration are replaced through disposal/re-registration. Concurrent calls on that registration queue in arrival order and execute in one process at a time. Registrations are bounded separately from workers. All profile/class/identity declarations are trusted host input, not plugin-controlled admission claims.

The scheduler selects eligible work from the tenant with the fewest active workers; least-recently-served tenant breaks ties. Free capacity is work-conserving across independent plugins, with no fixed normal-worker entitlement. Running work is not preempted when another tenant arrives. Fairness is admission fairness, not equal CPU or runtime; normal duration bounds and heavy caps limit interference. Queued reentrant callbacks are denied to prevent capacity/one-worker dependency deadlocks. Fan-out belongs in the consuming orchestration layer outside callbacks.

Normal and heavy calls have separate bounded deadlines. Queue waiting is outside these deadlines; existing adapter invocation timing still includes startup and callbacks. Heavy admission consumes at most half the configured memory budget and a configured count strictly below the total worker count. Heavy declaration and per-tenant concurrent-heavy limits are independently bounded. These are reservation controls, not native RSS enforcement.

Resident workers are an LRU cache, not a per-customer reservation. Eviction destroys the used process before capacity can move. Idle expiry and pressure eviction require reconstructible plugin state. Cleanup failure closes scheduler admission and remains visible; underlying quarantine keeps its reservation. A bounded pristine reserve contains one worker per recently demanded distinct resolved profile/version up to its global cap; no registration/demand means no reserve. This deliberately conservative initial policy avoids speculative process storms. Per-profile target prediction is not claimed.

Queue cancellation/expiry is swept at approximately 100 ms. Payloads are frozen and bounded by count and serialized byte size; these limits bound retained work but are not a complete host-memory ceiling. Count/RSS/queue metrics must accompany workload-dependent sizing.

## Measurement and initial review gate

Use a separately identified Apple M4 MacBook Air, 32 GiB physical RAM and the existing approximately 24 GiB Docker allocation. Preserve background services. First measure a small native/Docker Python pilot with 4/8/16 active customers and eight total workers. Each run records artifact/source hashes, per-tenant outcomes, total/queue/execution latency, cold-call count, eviction count, resource samples and cleanup. Retain failures and stop growth at the first failed quality stage. This pilot is not a server maximum or a comparison with M2 Ultra SDK/callback workloads.

For a realistic operational boundary, vary registered population, active working set, arrival rate, payload, language and heavy mix independently. Fixed-arrival tests include generator lateness in latency and count generator drops; closed-loop tests alone cannot establish supported arrival rates. Define SLO/error/memory/queue policies first, bracket pass/fail points, refine and repeat in alternating order, then soak the candidate boundary. Leave expansive runs for review after first evidence as requested.

The user clarified that the primary benchmark must represent many infrequent customers. The initial 4/8/16 stress runs remain diagnostic controls. The revised pilot registers 100/500/1,000 customers, schedules one call per customer per minute with seeded independent random phases, performs no warmup, and shares at most 32 resident workers. This varies population while holding individual demand fixed. Keep population results separate from saturation throughput; neither a single period nor one observation per customer establishes a production p99 or a maximum.

## Alternatives and limits

Cross-tenant reuse of a used process was rejected: it violates current ownership and has no reset attestation. Always destroying after each call would throw away useful temporal locality. Always retaining all workers would defeat the density goal. Initial LRU can thrash with a uniformly active population larger than the cache; the pilot intentionally exposes this rather than hiding cold work behind an unlimited warmup.

Weighted CPU fairness, multi-call protocol multiplexing, distributed placement and preempting durable heavy jobs remain outside this implementation. Docker CLI lifecycle cost and native process start cost are measured separately. Docker memory and macOS RSS are never added into one physical-memory claim.

Inspection of `LocalPluginClient` found that SDK streams keep iterator state across start/next/close wire operations. The first candidate explicitly rejects these operations before dispatch rather than evicting state between batches. Complete-stream residency leases and SDK-client gate integration require a follow-up before using this scheduler for that surface. Unary calls and the legacy direct-host stream path remain available.

## Technical references

- .NET asynchronous completion and continuation ownership: https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1?view=net-10.0
- .NET supported deadline range: https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.-ctor?view=net-10.0
- Existing `docs/worker-lifecycle.md`, `docs/dotnet-guidance.md` and `docs/soak-testing.md` define preserved runtime boundaries.

## Memory-led population correction

The scheduler defaults to no independent worker-count ceiling. Admission uses the sum of declared profile memory reservations; optional explicit count limits exist for controlled experiments. Simultaneous launches are bounded separately and wait within the invocation deadline. Default idle retention is two minutes to avoid forced cold starts for once-per-minute customers. This candidate requires an application-assigned deployment memory budget and does not infer capacity from instantaneous RSS or auto-discover OS free memory.
