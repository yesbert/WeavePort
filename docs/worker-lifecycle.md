# Shared worker lifecycle

The hosting package provides one shared, bounded pristine reserve per `PluginHost`. Use one coordinator for each intended node budget. Several hosts have independent budgets; this is not a distributed allocator or an automatic machine-wide singleton.

```csharp
await using var host = new PluginHost(
    maximumCallsPerTenant: 4,
    options: new WorkerPoolOptions(
        MaximumWorkers: 32,
        MemoryBudgetMiB: 8192,
        MaximumPristineWorkers: 4,
        MaximumConcurrentStarts: 4,
        MaximumWorkersPerTenant: 4,
        MemoryBudgetPerTenantMiB: 1024));

var profile = new DockerProfile(
    "weaveport-poc-python:1",
    IdleTimeout: TimeSpan.FromMinutes(2));

await host.PrewarmAsync(profile, version: "1", count: 2);
await using IPluginSession session = await host.BindAsync(
    context, profile, callbacks, grants);
InvocationResult result = await session.InvokeAsync("echo", payload);
```

The consumer supplies `PluginContext`, `IHostCallbacks`, granted operations and a `JsonElement` payload. Use `WeavePort.Abstractions` and `WeavePort.Hosting`. Omit `IdleTimeout` for stateful process-affine usage; enabling it means the application can tolerate loss of worker-local state and reconstruct required state on the next invocation.

## Capacity and pool behavior

Defaults are 64 workers, 16,384 MiB of summed configured worker ceilings, eight concurrent starts, at most four pristine workers, eight assigned workers and 2,048 MiB per tenant. Maintenance runs every second; unused pristine workers expire after 30 seconds. No warm target is configured automatically, so the default does not speculatively launch workers. Limits are coordinator policy values, not machine sizing recommendations.

`PrewarmAsync` sets a persistent target by resolved image digest, plugin version, Docker context and sandbox resource profile. Targets share the global pristine ceiling; they are not multiplied by customer count. Zero removes a target. Initial fill and later replenishment obey the same budgets as real work. Read `Snapshot.Pristine` to see achieved readiness when resource pressure prevents a full target. Failed configuration attempts restore the prior target. Under capacity pressure, actual demand can reclaim unused pristine workers for another image; used or quarantined workers are never reclaimed for assignment.

Ready workers have received no customer context, credentials or callback authority. Each checkout is exclusive and sets its tenant owner once. Its immutable session supplies the same context and grants throughout that binding. Warm, used instances stay with their binding; they never return to a customer-shared reserve. A configuration, principal, grant or secret change requires disposal and a new binding. A changed image tag does not silently change an existing binding's resolved digest.

Optional host logging identifies admission refusals with event 1006 and fixed reasons: `concurrent-starts`, `pool-reservations`, `tenant-reservations`, `session-call` or `tenant-calls`. No payload or caller-supplied reason is logged.

Admission counts starting, assigned, pristine and cleanup-uncertain workers globally, and assigned/starting/cleanup-uncertain workers against their tenant. Reserved memory sums configured container ceilings; it is not measured Docker residency or RSS. Exceeding worker, memory, tenant or concurrent-start admission returns `busy` without dispatch. Hosts still need sizing headroom and application-level overload handling; caps do not eliminate shared CPU/engine interference or ensure fairness across an unlimited number of tenants.

## Release, callbacks and diagnostics

`IPluginSession.Instance` is the last assigned identifier, not a liveness probe; it can name an already removed environment until the next invocation.

Idle maintenance only stops opted-in sessions after their call gate is free. Their binding stays valid; a later call acquires a fresh environment. Default bindings preserve local state until explicit restart/disposal. An injected `TimeProvider` controls idle age and maintenance scheduling. `MaintainAsync` can trigger a sweep explicitly.

Disposal removes the host's session registration. Shared tenant admission records remain while any binding or detached callback references them. Nested calls from a completed or cancelled invocation scope are denied, and callback arguments retain their original immutable authority. Callbacks that ignore cancellation may still finish external actions: the consumer must handle uncertain outcomes and idempotency.

Worker removal attempts `docker rm --force` and confirms absence if Docker reports failure. Unconfirmed removal leaves the reservation quarantined, even after the client process is stopped. Maintenance retries cleanup; `Snapshot.Quarantined` exposes the retained count, including destruction in progress. `Snapshot.OldestQuarantineSeconds` reports the oldest pending removal age from first quarantine entry, retaining age across retries and reporting zero when none remain. `MaintenanceFailure` retains the last background failure type. Explicit maintenance/disposal surfaces failures. No quarantined worker is eligible for checkout. Container deletion removes its private writable layer/tmpfs; immutable images may remain cached.

## Verification and limits

`./scripts/verify.sh` includes packed API scenarios for simultaneous customers, private markers, secret/callback identity, quotas, automatic replenishment, idle/default state, active-call protection, expiry, version failures, registration churn, late callbacks and simulated cleanup failure. Focused transport checks exercise split/multiple frames, UTF-8, byte/depth limits, ownership, cancellation and atomic output rejection.

`./scripts/lifecycle.sh` records a separate 128-customer resource experiment. `PristineStartBenchmarks` measures first-call latency with/without a ready reserve; reserve construction is outside that timed call and has a real CPU/memory cost. See [benchmark boundaries](benchmarking.md), [capacity testing (historical) — pre-public record](history.md) and the [original design exploration](architecture.md).

The current runtime does not implement predictive pool sizing, distributed placement/fencing, checkpoint storage or safe cross-tenant reuse of a used process. The default stdio transport retains one Docker CLI process per running worker; the opt-in [Linux socket transport (historical) — pre-public record](history.md) uses short-lived CLI commands for lifecycle operations. Container isolation does not establish protection against kernel/engine failure or zero latency interference from another customer.

The [trusted local adapter](local-execution.md) shares these lifecycle rules. Its memory reservations are admission estimates, not OS-enforced ceilings; root-process cleanup does not attest termination of escaped descendants. Docker-specific enforcement statements above apply only to Docker profiles.
