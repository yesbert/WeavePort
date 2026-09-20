# Approved session reuse: implementation verification

Development candidate, macOS arm64, 20 September 2026. No packages were published. The source remains an uncommitted candidate over the base revision in [provenance](provenance.json); pre-existing scheduler/research changes are retained.

## Implemented contract

Default `CustomerBound` workers retain their immutable binding. Operator-selected `ApprovedSessions` requires the native cleanup capability and successful acknowledgement before compatible sequential reassignment. Reuse accounting, idle expiry, explicit restart, disposal ownership and fair scheduler residency are integrated. Streams retain their worker until closure. Failure/cancellation retires the worker. MCP cannot opt into this native extension.

All three SDKs provide registered resources and reverse-order cleanup. Hidden unregistered globals deliberately remain observable in the approved negative control. This qualifies a cooperative operator-approved policy, not malicious-code confinement. See [author/operator guide](../../../docs/reusable-plugins.md) and [runnable examples](../../../examples/reuse/README.md).

## Verification

- 976 source and 976 isolated packed consumer assertions.
- 324 assertions for each Docker provider: Python, TypeScript and C# (972 executions).
- 382 packaged multilingual SDK regression checks, including client/gateway transports.
- 88 scheduler assertions and the existing Hosting regression suite.
- Six package/public-API checks, two negative review gates and nine example protocol calls.
- Code style, documentation links, public-tree checks and specification validation.

Logs are adjacent. [Final assembly identities](assembly-identity-final.json), [image identities](images.json), [source/package provenance](provenance.json) and the [reviewed API diff](api-diff-final.txt) retain the verification boundary. The public snapshot is additive; the published eight-argument WorkerPoolOptions constructor/deconstruction remain present. Provider images use the verified SDK bytes; the controlling host runs outside the provider container. C# provider-image Hosting dependencies are not the controlling host.

The final measurement follows the ABI-preserving options correction. The initial cold runs retain their earlier Hosting hash, explicitly recorded in the measurement file. They are not silently relabeled as measurements of the final assembly.

## Actual-host population measurements

Each customer performs two measured calls in 60 seconds, with deterministic random arrival phase and latency from intended arrival. The Python SDK fixture returns the bound tenant/configuration and registers mutable-buffer cleanup. It performs no business database/network work. Memory budget is 512 MiB at 64 MiB reservation per worker, with no independent worker-count limit. This yields at most eight workers for this experiment, not a product-wide cap.

| Run | Customers | Calls/s | p99 ms | Maximum ms | Host/load generator MiB | Container working set MiB | Acceptance |
|---|---:|---:|---:|---:|---:|---:|---|
| density-native | 30000 | 1000.01 | 33.13 | 219.06 | 207.28 | — | pass |
| density-docker | 6000 | 200.00 | 526.62 | 1941.65 | 96.30 | 129.17 | fail |
| density-final-native | 30000 | 1000.00 | 31.52 | 58.08 | 220.47 | — | pass |
| density-final-docker | 1000 | 33.33 | 33.79 | 247.38 | 79.50 | 93.41 | pass |

Every run completed all intended calls with correct customer/configuration and no call errors or drops. The initial cold Docker run fails the stricter per-customer p99 target: five customers exceeded one second during initial container startup, despite aggregate p99 below one second. Preserve this failed result. With only two calls/customer, that per-customer percentile is essentially the worse call.

Warm runs explicitly execute eight preparatory calls outside measurement; setup duration and counters remain recorded. The final Docker warm run is a small operating-point check, not a capacity test comparable in scale with the 6,000-customer cold run. The low-load pool can retire unused workers under its idle policy.

Docker registration for 6,000 customers took about 293 seconds because image resolution is repeated per registration (including the current scheduler/host handoff). Registration occurs before the throughput timer. Reusing pinned deployment resolution without changing tag/pin semantics is follow-up optimization work. Reserve/warm relevant reviewed deployments before strict latency-sensitive traffic; an initially empty pool still has container startup cost.

Container working-set/raw cgroup memory is separate from macOS host/load-generator and Docker CLI RSS. Native child RSS may double-count shared pages. The host had substantial pre-existing swap usage; a 256 MiB swap-growth guard and RSS ceilings were enabled. These one-minute runs do not establish a maximum, sustained server capacity, hostile-code isolation or performance on other platforms. The earlier experimental Python harness numbers are not substituted for actual Hosting/SDK throughput.

[Structured measurements](measurements.json) include per-customer success bounds, latencies, worker starts/reuse, cleanup, registration/warmup duration, hashes and exact raw-artifact locations. Full raw per-customer records remain in the local artifacts directory; the checked-in report retains compact aggregates and hashes to avoid adding tens of megabytes of repetitive samples. All observed owned benchmark containers were gone after disposal.

## Release boundary

Version labels are unchanged local candidate labels (core 0.3.1; Python/TypeScript 0.1.0). Fresh isolated caches prevent substitution of old same-version packages. Publishing/version assignment, release-from-committed-HEAD checks, Windows qualification and maximum-capacity/long-duration measurement are not claimed here.
