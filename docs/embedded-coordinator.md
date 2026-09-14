# Embedded coordinator template

Use one coordinator to share worker and operation budgets across your application. Appointment Desk demonstrates this with one `EmbeddedCoordinator` at its composition root. All its admitted booking operations share that coordinator's `PluginHost`, worker limits and tenant admission. The copyable [consumer source](../samples/Shared/EmbeddedCoordinator.cs) is linked into the host project; it adds no public NuGet API or service dependency. [Desk](../samples/AppointmentDesk/Host/Desk.cs) demonstrates passing the shared host into operation code and disposing only the operation's client/session.

## Compose an application

```csharp
var coordinator = new EmbeddedCoordinator(
    maximumOperations: 8,
    options: new WorkerPoolOptions(
        MaximumWorkers: 8,
        MemoryBudgetMiB: 2048,
        MaximumPristineWorkers: 0,
        MaximumConcurrentStarts: 4,
        MaximumWorkersPerTenant: 2,
        MemoryBudgetPerTenantMiB: 512));

// Use this same instance for every incoming operation in the intended budget.
var result = await coordinator.RunAsync(async (host, token) =>
{
    await using var session = await host.BindAsync(
        context, executionProfile, callbacks, grants, token);
    return await session.InvokeAsync(operationName, payload, token);
}, requestCancellation);

// First close ingress, then stop the coordinator before disposing callback services.
var stopped = await coordinator.StopAsync(
    grace: TimeSpan.FromSeconds(5),
    observation: TimeSpan.FromSeconds(5));
if (!stopped.Clean)
{
    // Report Snapshot; keep callback dependencies alive while work remains.
    // Completion observes coordinator tasks, not necessarily detached callbacks.
    // Apply the application's explicit escalation policy; never silently replay effects.
}
```

Copy/link the source into the consuming application and adapt the composition settings. The variables in the invocation example are supplied by that application's authenticated request and approved installation resolution. Limits above illustrate configuration, not node sizing. Multiple coordinators still have independent budgets. A native memory reservation is an admission estimate, not an OS memory ceiling. Keep worker/runtime files stable for the operation and any recovery lifetime.

The delegate must await all its work, dispose its sessions before returning, and never dispose or retain the injected host. Fire-and-forget work escapes application operation accounting. Callback services and stores outlive every callback using them. The optional internal disposal delegate exists only for deterministic failure verification; normal composition uses the owned host's disposal.

## Admission and outcomes

Operation admission is immediate: there is no internal queue and no implicit retry. At the operation limit, `CoordinatorRejectedException.Status` is `busy`; after shutdown starts it is `stopping`. Neither refusal runs the delegate, creates a binding or changes domain state. Pre-cancelled requests do not run. Concurrent admission reserves a slot atomically. Accepted work retains its slot through its entire delegate, including awaited session disposal, even if cancellation was requested.

The underlying shared host can independently refuse worker/start/memory/tenant admission with runtime `busy`. In Appointment Desk this can happen before proposing a slot and creates no intent. A failure during execution of an already persisted command stays `uncertain` under the application's existing reconciliation rules. Do not translate all overload, cancellation or cleanup exceptions into a definite failed effect.

`Snapshot` includes active operation count, returned/failed delegate counters, acceptance state, shutdown/cancellation state, cleanup error types and the existing `WorkerPoolSnapshot`. `Completed` counts delegates that returned, including a domain `uncertain` result; it does not count successful business effects. `Failed` counts exceptions from accepted delegates; refused admission is excluded. Correlate outcomes with the application's request identity without logging plugin payloads or secrets.

## Shutdown and ownership

The first `StopAsync` call closes admission atomically and fixes the grace interval. Accepted operations may finish normally during grace. At grace expiry the coordinator requests cancellation and begins host disposal, tracking cancellation callbacks and cleanup separately. No cancellation callback is allowed to delay the start of host disposal. Each call waits at most its supplied grace plus observation intervals under the supplied `TimeProvider`, subject to runtime scheduling; this bounds observation rather than promising OS termination at that instant. Use zero grace on subsequent observation calls if no additional grace-sized wait is desired. The original lifecycle is never restarted.

A wait timeout returns the current snapshot and preserves the live `Completion` task and active count. `Completion` means coordinator shutdown tasks finished; inspect a fresh `Snapshot.Clean` as well. An invocation callback that ignores cancellation may outlive its worker/session and keep `Runtime.Tenants` nonzero even after `Completion`. `Clean` additionally requires zero active operations, workers, bindings and tenant records, with no recorded cleanup or maintenance failure. Cleanup errors remain diagnostic even if resource counters later reach zero. Simulated cleanup failure is tested; this slice does not newly qualify OS-level deletion failures.

`DisposeAsync` uses five seconds grace plus five seconds observation and throws if shutdown is not clean. For a service host, use explicit `StopAsync` and inspect the result before releasing dependent services. Simply catching a disposal exception and disposing callback dependencies can race outstanding work. Uncooperative code needs an application/deployment escalation decision; the template cannot safely force arbitrary managed callbacks to stop. It never reports cancellation as proof that an external action did not occur.

The [native operations runbook](native-operations.md) now adds a persistent run guard to normal Appointment Desk execution and requires supervised recovery after a crash. Escaped descendants, automatic orphan cleanup, power-loss durability and distributed admission remain unqualified.

## Verify

```sh
./scripts/appointment-desk.sh --build --verify
./scripts/verify-compatibility.sh
```

The second command assumes the other reference applications and SDK artifacts have already been built as documented in [package compatibility](package-compatibility.md). The Appointment Desk suite uses packed NuGet dependencies and actual installed native workers for shared quotas, independent customers, worker loss, graceful drain and forced shutdown after booking. Additional consumer checks exercise concurrent admission, uncooperative delegates and simulated cleanup failure. See [retained evidence (historical) — pre-public record](history.md).
