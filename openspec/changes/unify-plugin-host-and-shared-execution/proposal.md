## Why

Installed multilingual plugins need one understandable path from a verified installation to bounded execution. Long-lived computing plugins must share expensive immutable state across concurrent tenant calls, while tenant-bound connectors retain exclusive state and can stream large source objects.

## What Changes

- **BREAKING**: fold scheduled admission into one PluginHost and one budget; replace the public ScheduledPluginHost and clarify explicit worker retention.
- Add operator-approved Shared ownership alongside unchanged customer-bound and cleaned sequential ownership, with resident workers, bounded concurrent calls, per-invocation context, cancellation, failure isolation and restart suppression.
- Extend verified catalogs with contract listing and launch declarations; combine declarations with one operator approval and return a bound SDK client directly.
- Add operation-wide stream residency, immediate available-item flushing, keep-alives, separate exchange/whole-stream deadlines, and configurable callback limits.
- Add bounded collection of plugin-originated bytes into existing composition result scopes.
- Qualify all three SDKs, document migration, provide executable samples, audit consistency, measure performance and publish the next complete seven-package minor release.

## Capabilities

### New Capabilities

- `shared-plugin-execution`: explicit resident concurrent execution and its admission, identity, failure and recovery contract.

### Modified Capabilities

- `fair-scheduling`: one host, invocation-based fairness and stream residency.
- `worker-lifecycle`: shared ownership and explicit lifecycle policies.
- `tenant-isolation`: approved simultaneous sharing without weakening existing ownership.
- `plugin-execution`: per-invocation authority and negotiated concurrent protocol.
- `plugin-sdk`: multiplexed SDK execution, live streams and configurable callback budget.
- `installed-plugin-resolution`: multi-plugin discovery and approved launch declarations.
- `bulk-composition`: collect plugin-originated bytes using existing bounded result storage.
- `runtime-diagnostics`: shared worker state, bounded optional diagnostics and recovery visibility.

## Impact

Hosting, abstractions, all author/client SDKs, Composition, Gateway integration, sealing tools, compatibility baselines, tests, benchmarks, documentation and samples. No new runtime dependencies are intended. TreeWeaver domain adapters, in-process extension loading, hostile-code sandboxing, distributed scheduling and hot release replacement remain outside this change. Shared SDK result streams are initially refused; live streaming and large collection remain supported on exclusive bindings.
