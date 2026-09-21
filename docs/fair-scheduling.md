# Fair scheduling and customer density

Use one `PluginHost(new SchedulingOptions { ... })` for a shared worker budget and bounded fair admission. `QueueTimeout = TimeSpan.Zero` selects fail-fast admission. The direct limits constructor remains available for immediate admission. Do not create a host per customer.

Exclusive bindings serialize calls. SDK clients use operation-wide leases for streams and binary sources, so worker residency survives every exchange and consumer pause. Shared installations use resident workers and admit concurrent unary calls up to their approved degree. The recommended application entry point is [a verified installation and one operator approval](installed-plugin-clients.md).

```csharp
await using var host = new PluginHost(new SchedulingOptions
{
    MemoryBudgetMiB = 4096,
    MaximumHeavyCalls = 2,
    MaximumHeavyCallsPerTenant = 1,
    NormalTimeout = TimeSpan.FromSeconds(5),
    HeavyTimeout = TimeSpan.FromMinutes(5),
    IdleTimeout = TimeSpan.FromMinutes(2)
});

// context, profile, callbacks and grants come from the authenticated application.
await using IPluginSession plugin = await host.BindAsync(
    context, profile with { WorkClass = PluginWorkClass.Normal, Reconstructible = true }, callbacks, grants);
InvocationResult result = await plugin.InvokeAsync("operation", payload, cancellationToken);
```

`MaximumWorkers` defaults to null: there is no independent worker-count ceiling. Admission reserves each profile's configured memory against `MemoryBudgetMiB`; the count follows demand and profile size. `MaximumConcurrentStarts` (default eight) limits launch concurrency, not resident workers, and excess starts wait within the invocation deadline. An explicit worker ceiling remains available for controlled comparisons. Idle eviction defaults to two minutes for explicitly reconstructible profiles; memory pressure can evict those workers sooner. Default customer-bound state retains affinity.

These are policy examples, not node-sizing recommendations. Docker enforces configured per-worker ceilings; native profiles only reserve estimated memory. Other processes, Docker overhead, the host and load generator require additional headroom. The application must assign the budget from its deployment allocation after that headroom; the scheduler does not automatically discover free OS memory or admit against instantaneous RSS. Low current RSS does not authorize exceeding the sum of declared reservations.

## Developer contract

- Set `Reconstructible = true` only when idle state can be rebuilt. Those workers may be evicted earlier than `IdleTimeout` under pressure; default customer-bound workers retain state. Persist durable state externally in either case.
- Parallelize related subtasks inside one invocation. Concurrent independent calls to the same customer-plugin registration queue in order; they do not start additional instances. Shared ownership instead permits negotiated concurrent unary invocations.
- Orchestrate independent plugins with fan-out/fan-in outside plugin callbacks. Each admitted plugin uses its own exclusive worker. Calls from callback scopes are denied consistently to avoid dependency deadlocks.
- Keep normal operations short. The host caps the profile timeout by the normal/heavy policy. The deadline starts after queue admission and includes worker startup, transport and callbacks; it is not a pure plugin-CPU deadline.
- Declare heavy plugins through trusted application policy. Plugins and end users must not promote requests to heavy without authorization. Global and tenant heavy-call limits, a declaration limit and a half-budget memory cap preserve room for normal work.
- Handle `busy`, `cancelled`, `timeout` and shutdown explicitly. Queue overflow/expiry does not execute; invocation timeout may occur after external side effects. Retry only under your application's idempotency rules.
- Dispose before changing a customer's plugin version, grants, identity or configuration. One `(tenant, plugin)` registration exists at a time; separate names must correspond to genuinely independent authorized plugins.

## Distribution and residency

Among eligible queued calls, the customer with the fewest active invocations goes next. Least-recently-served customer breaks ties. This gives admission fairness and lends unused capacity to a lone customer. It does not promise equal CPU time or immediate preemption of running work.

Explicitly reconstructible idle workers form a bounded LRU cache. Pressure can evict these before starting another. A stream lease prevents eviction even while its consumer is paused. Shared workers remain resident until disposal or failure. Customer-bound used processes never move between customers. Approved SDK unary calls release clean workers to a compatible shared pool; these bindings no longer require individual idle residency. A uniform workload with more simultaneously active customer-plugin pairs than worker slots can therefore spend most of its time restarting processes. Increasing registered-customer count and increasing the active working set are different capacity questions.

The optional pristine reserve uses recently requested registered profile/version pairs, with one target per distinct pair up to the shared cap. Absent languages and unused profiles do not launch workers. Profiles include actual executable/image and dependencies; a language name alone is not a safe pool key. Maintenance adjusts targets when the dispatcher is idle; old targets expire after demand disappears. This is a conservative demand policy, not predictive sizing.

Queue counts and per-call serialized byte bounds cap retained requests. Defaults permit up to 4,096 queued calls of up to 1 MiB each, so choose tighter limits when host memory or workload requires it. Queue cancellation/expiry is swept approximately every 100 ms. Exceptions during scheduler cleanup stop new admissions; inspect `host.Scheduling.Failure` and `host.Snapshot` instead of treating this as ordinary load shedding.

`ScheduledPlugin.InvokeMeasuredAsync` separates queue and execution time; `InvocationResult.ElapsedMs` includes both. `host.Scheduling` exposes queue/active counts, cold calls, evictions and rejection counts; `host.Snapshot` exposes underlying reservations. A cold-call count means the scheduler selected a nonresident registration without acquiring a previously used clean worker; it can still acquire a ready pristine process.

## Verification and sizing

Run `dotnet run --project tests/WeavePort.Scheduling.Tests -c Release` for public scheduler regressions. Repeat with `-p:UsePackedCore=true` against freshly prepared candidate packages for the package boundary.

See [density measurement](density-testing.md) for the supervised pilot and operational-boundary method. Current historical [SDK benchmarks](benchmarking.md) use a different workload and machine and cannot establish a speedup for this scheduler.

Dispatch indexes active and resident plugins separately from the customer registry. Dormant registrations do not add a full registry scan to every invocation. Registration and disposal update tenant counts under the scheduler lock. A [separate registry diagnostic](../benchmarks/WeavePort.Registry/README.md) measures this cost with fixed native workers; its hot-request rate is not a distinct-customer throughput claim.

The [extended MacBook Air evidence](../reports/benchmarks/reuse-matrix-air-20260919/README.md) records pool scaling, repeated arrival-rate boundaries, grouped customers, fault recovery, long-run memory, production-host controls and the corrected kernel-IPC boundary. Experimental reuse remains separate from the production host contract.

Scheduled foreground acquisition waits for relevant pending pristine capacity within the invocation deadline and takes precedence over replenishment. A background reserve must not turn an otherwise admissible low-load customer into a spurious `busy` result. Per-worker startup completion avoids waiting on unrelated foreground startups.

## Reviewed cross-customer reuse

Set `ExecutionProfile.ReusePolicy = WorkerReusePolicy.ApprovedSessions` only in operator-reviewed deployment configuration. The same shared scheduler admits the work; completed clean sessions return their worker and release binding residency. `SchedulingOptions.ReusableIdleTimeout` controls clean-pool idle retention. See [SDK resources and author best practices](reusable-plugins.md). The reviewed deployment must include the native cleanup-capable SDK; legacy and MCP protocols cannot opt in.

## Migration

Replace the former `ScheduledPluginHost` constructor with `PluginHost(SchedulingOptions)` and `RegisterAsync` with `BindAsync`. Put work class on the profile, select `Reconstructible` explicitly, use `host.Scheduling` for queue diagnostics and `host.Snapshot` for worker accounting. `LocalPluginClient` now follows host admission for unary calls.
