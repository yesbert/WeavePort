# Supervised k6 soak testing

The local soak drives the actual duplex `WorkerGateway.Session` protocol, packed host/client/gateway implementations and C#, Python and TypeScript SDK providers. It uses explicitly trusted native stdio workers and an HTTP/2 loopback gateway. It does not test remote TLS/network deployment, hostile plugins, native hard resource containment or maximum capacity. Docker services are not changed.

## Prepare and run

Use the pinned repository SDK, Python, Node and a local k6 executable (verified with k6 2.2.0 on macOS arm64). The Linux supervisor path is implemented but has not been qualified by this change; Windows supervision is not implemented. No k6 cloud account, extension or new application dependency is required. From the repository root:

```sh
./scripts/build-sdk.sh
./scripts/soak.sh --seconds 120 --clients 24
./scripts/soak.sh --seconds 14400 --clients 24
```

The build prepares development packages from this source revision, not the previously delivered release bytes. The runner verifies loaded DLLs against those packages and records their hashes separately from release qualification. Avoid rebuilding packages or editing fixtures during a run. Close unrelated benchmark/test runs first; retain ordinary background services and interpret measurements in that environment.

All bindings are warmed sequentially before k6 starts. The soak fixture explicitly configures concurrent-start slots equal to its VU count (24 by default), because fault/recovery cycles can make every binding need a replacement concurrently. The library default remains eight and is tested separately for pre-dispatch overload rejection. Iteration-based staggering alone cannot guarantee an eight-start ceiling. The effective fixture limit is recorded in result.json and can be lowered deliberately with `--max-concurrent-starts 8` for an overload negative control. Worker/memory/tenant limits and zero-error acceptance are unchanged. Cold lifecycle remains measured by the separate SDK benchmarks.

The default run lasts four hours with 24 independent tenant bindings, one virtual user per binding, languages assigned round-robin, and 100 ms pacing between iterations. Client count is bounded to 3–48 under the fixture's existing 64-worker budget. These defaults are a soak configuration, not a production sizing recommendation.

## Workloads and acceptance

Each iteration sends sequential echo, immutable-owner callback and result-stream exchanges on one duplex session; the connection is reused across iterations and periodically reconnected. Every twentieth iteration reads a complete 8 MiB text result; other iterations read 64 KiB. Every row's index, contents and owner are checked; batches and terminal completion must be valid. JSON/envelope overhead is additional.

Staggered fault cycles exercise provider exceptions after partial output, denied callbacks, worker crashes, subsequent successful calls, and cancellation through a second control session. Cancellation waits for the original stream terminal and cleanup acknowledgement before reusing the binding. Expected faults have a separate counter. This tests the gateway protocol directly; the .NET remote client's own pooling and abandoned-enumeration behavior remain covered by the SDK/gateway regressions.

No unexpected errors are accepted. Every tenant and every configured operation kind must complete work. Default whole-run p99 is below 2,000 ms; this is an experiment acceptance policy, not a product SLO. Latency includes full result validation; cancellation includes drain and acknowledgement. k6 also retains per-tenant/per-kind statistics. Each duplex session has a 30-second deadline, and k6 has a bounded graceful stop.

## Resource and stop controls

The supervisor samples the gateway's worker/binding/tenant accounting, managed heap, allocation counter, GC counts and reported handles. It samples process-group RSS and `ps` CPU for the gateway, its child workers and k6 every five seconds, retaining each process separately. RSS can double-count shared pages and miss short peaks; CPU is the platform's `ps` observation, not a precise interval utilization counter. Escaped descendants are outside this trusted-fixture qualification. Actual gateway/k6 descriptor counts are sampled through `/proc` or local `lsof` when available (otherwise null). A platform-reported managed `HandleCount` of zero is not proof that no descriptors are open. Allocation counts cover only the gateway's managed process, not the language workers or k6.

Defaults stop the run on:

- Any unexpected k6 error or a failing global, tenant or workload latency threshold after 30 seconds.
- Summed sampled RSS above 8,192 MiB.
- RSS growth above 2,048 MiB relative to the first sample after five minutes.
- Background maintenance failure or an individual pending removal older than 30 seconds (`OldestQuarantineSeconds`). Overlapping short removals do not accumulate age; retries of the same removal do. Missing or invalid cleanup diagnostics fail closed.
- Gateway exit, control-channel timeout, missing results or an overall run deadline.

Adjust explicit test policy with `--p99-ms`, `--max-rss-mib`, `--max-growth-mib`, `--growth-after`, `--pace-ms` and `--sample-seconds`. RSS growth is a conservative early-stop signal, not proof of a leak. k6's aggregate percentiles may hide short stalls; inspect the chronological data too.

The runner prints its output directory. To stop it deliberately, press Ctrl+C or create a file named `STOP` in that run directory. The supervisor interrupts its k6 process, asks its gateway to shut down, and uses bounded process-group termination only for those fixture-owned processes if necessary. Forced cleanup, leftover processes/workspaces, interruption or missing evidence cannot become a successful run. An operator/agent can inspect the live evidence and use the same stop file when problems appear.

Credentials exist only in a private temporary configuration file and the launcher's control pipe; they are not included in reports. The runner binds the test gateway and k6 control endpoint to loopback. Do not point this synthetic workload at a production gateway.

## Analyze results

```sh
python3 -B tools/soak/analyze.py artifacts/soak/<run>
```

This writes `report.md` alongside:

- `result.json`: outcome, stop reason, source/package/runtime identities, policy and cleanup result.
- `k6-summary.json`: operation counts, error rates and complete-operation latency distributions by tenant and workload.
- `resources.jsonl`: time-stamped process and coordinator observations.
- `k6.log`: bounded per-tenant minute windows (completed operations/max latency) and diagnostics.
- `gateway.log` and `source.patch`: existing safe host failure events and tracked source differences. k6 error lines include fixed validation reasons and allowlisted status codes; no raw transport exception text is exported.

Compare stable intervals after warmup, worker counts following fault cycles, GC/heap and RSS trends, tenant tails and any retained quarantine. Confirm that all tenants served work and final cleanup succeeded. A short pilot validates the harness; only a completed multi-hour run can provide multi-hour stability evidence.

## Harness regression controls

```sh
python3 -B tests/soak/test_support.py
./scripts/soak.sh --seconds 60 --clients 3 --inject-failure
./scripts/soak.sh --seconds 60 --clients 3 --max-rss-mib 1
```

The report includes the absolute number of unexpected error samples (an operation and its enclosing iteration may each contribute) and rates with eight decimal places, so rare failures do not disappear behind three-decimal rounding.

For a focused native Python crash/replacement regression after `build-sdk.sh`:

```sh
./scripts/sdk.sh artifacts/crash-regression-<new-run> verify --crash-stress
```

This performs 6,000 crash/replacement cycles across three tenants, checking an echo before each injected crash. It is separate from the mixed gateway soak.

Both negative-control commands above must exit nonzero and retain the failing result and cleanup evidence. They must not be reported as successful soak measurements.
