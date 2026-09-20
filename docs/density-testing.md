# Supervised customer-density measurements

This experiment measures the new optional scheduler separately from the retained M2 Ultra release benchmarks. It is a source-built candidate on the current machine, not qualification of published 0.4.0 binaries. The first pilot uses a MacBook Air M4, 32 GiB physical RAM, ten CPU cores and the existing approximately 24 GiB Docker allocation. Existing background services remain running.

## Customer-population pilot

The primary sizing question is many customers making few calls each, not a few customers saturating their workers. Use a seeded random phase per customer, one call per customer per minute, no prewarmed bindings, and a shared memory budget independent of customer count:

Build the harness and the exact fixture image before measuring:

```sh
dotnet build benchmarks/WeavePort.Density -c Release
docker build -f plugins/python/Dockerfile -t weaveport-poc-python:1 .
python3 tools/performance/density.py --output artifacts/density/air-population \
  --traffic population --calls-per-minute 1 --clients 100,500,1000 \
  --memory-mib 12288 --worker-memory-mib 64 --seconds 120 --repeats 1 --adapters native,docker
```

Use a new output directory for each invocation. The fixture returns the actual bound customer context including a configurable text payload. Every complete result checks tenant and text. This is a synthetic short native-protocol operation with a more representative access pattern, not a real application's business workload or the historical SDK callback/stream workload. One customer-plugin pair is registered per customer. Registration time is reported separately. Population mode includes every first start and schedules each customer independently of previous response completion. All customers receive a call during a full period; tests shorter than one period are rejected. The seed increments per repeat.

The default pilot has no pristine reserve to expose cold execution cost. Use `--pristine 2` for an explicit reserve control; the production scheduler's default is two shared pristine slots. After the initial review, run multiple periods to observe returning customers, alternate seeds and adapter order, and add a burst/heavy workload. A single 60-second period is an initial density observation, not a steady-state or multi-hour capacity qualification. With only one call per customer, that customer's reported p99 is effectively its single observed latency, not a statistically established customer SLO.

## Technical saturation controls

`--traffic saturation --clients 4,8,16 --workers 8 --seconds 8` retains the initial closed-loop stress experiment. One outstanding call per client drives the next call immediately after completion. The first cache-sized set is warmed; additional customers retain cold-start cost. These controls deliberately expose cache thrashing but do not represent the intended infrequent-customer traffic. No worker is shared between customers.

`--modes direct,scheduled` provides a same-machine direct-host control when clients fit the worker budget. A direct-host oversubscription failure is not a measured scheduler speedup. `--language` selects Python, TypeScript or C#; prepare that language's image and native fixture before using it. `--payload-bytes 65536` measures a larger context result. Transport, runtime and fixture identities must be kept separate across these variants.

Use `--registered` to separate the registered population from the active `--clients` working set. Unused registrations must not start processes. The initial pilot measures a fully active population; it does not imply that all registered customers are continuously busy.

## Operational boundary, not an absolute maximum

1. Define the workload and policy: registered customers, simultaneously active customers, independent plugins per customer, calls per second per active customer, result size, normal/heavy mix, and acceptable per-customer p99/error rate.
2. Establish a warm baseline and a cold/replacement baseline on this machine. Fix the memory budget and per-profile reservations while increasing the active working set. Also vary idle time and reserve size.
3. Run a fixed-arrival staircase with `--rates 100,250,500,1000` and a fixed population. This reports end-to-end latency from intended arrival, including generator lateness, queue and execution. Offered requests that exceed the bounded generator backlog are counted as drops, not silently skipped.
4. Stop growth at the first failed stage. All customers must be served, all results correct, no errors/drops, latency policy satisfied and cleanup confirmed. Resource/observer/operator failure invalidates the stage. Retain failed evidence.
5. Refine between passing and failing levels, repeat with reversed ordering and longer stages, and require every repeat to pass. A passing final stage is only a tested lower bound; an isolated failure is not proof of the absolute server maximum.
6. Run the selected operating point for hours, with a mixed heavy/fault/burst workload and background services, before publishing a capacity recommendation. The initial pilot intentionally stops before this expansion for review.

The runner stops further stages in that adapter/mode after a failure. `--seconds` is bounded to 600 per stage; `--clients` to 4,096. Worker count is uncapped by default; `--workers` is an optional comparison control. These are experiment safety bounds, not product capacity declarations. The revised population command reserves at most 12 GiB for workers, with a 64 MiB Python-fixture reservation per worker and no independent count ceiling. This can retain up to 192 fixture workers if demand requires them. It leaves part of the Docker VM's approximately 24 GiB allocation for existing services and overhead. Reassess headroom on each machine; the budget is not auto-detected. `--idle-ms` defaults to 120000, so returning customers at one call/minute can retain workers until memory pressure requires eviction. Few calls per customer do not imply few calls in total: 100/500/1,000 customers at one call/minute offer approximately 1.67/8.33/16.67 requests per second.

## Evidence and controls

Each stage retains config, console output, per-tenant counters, approximately 1% logarithmic request histograms summarized as p50/p95/p99/max, separate queue/execution/arrival-lag timing, cold calls, evictions, sampled peak worker count, host/runtime identity and cleanup. Throughput includes draining admitted work; the configured offered rate and actual elapsed duration remain explicit. Host/load-generator allocations and memory are not plugin memory.

The native observer samples the measuring host plus owned child-process RSS approximately every 500 ms. It stops on host RSS above 1,536 MiB or summed owned RSS above 4,096 MiB. Shared pages can repeat and short peaks can be missed. In Docker mode these macOS children include Docker CLI processes, not container memory. The supervisor separately records Docker's reported container memory for observed owned instance names; transient starts/removals may be missed. Docker CLI memory accounting and macOS RSS must not be combined into a physical-memory total.

The macOS supervisor also stops on system free percentage below 10%, swap growth above 512 MiB, observer failure, an overall wall deadline or a `STOP` file in the run directory. It records preexisting services and leaves them running. Write a `STOP` file to request controlled termination. Forced termination is an invalid run; it never becomes a passing measurement or authorizes deletion of unrelated containers.

The configured aggregate budget and per-worker reservations provide an additional bound; the lightweight Python fixture defaults to 64 MiB, which is not a recommendation for arbitrary plugins. Inspect observed memory and startup cost before choosing a larger reservation budget. Native reservation values do not enforce OS memory ceilings. Run `python3 tools/performance/test_density.py` and, after building the harness, `python3 tools/performance/test_density_integration.py` to exercise observer failure, resource-stop cleanup and generator-drop rejection.

## Initial evidence

See [memory-led MacBook Air observation](../reports/benchmarks/fair-scheduling-air-20260919/README.md). The native 100-customer two-period run passes with 100 resident workers. The initial Docker observer timed out under population growth; those stages remain invalid. The candidate now uses bounded Engine one-shot sampling and a sparse recorder. New population controls and their remaining limits are documented in the [extended Air report](../reports/benchmarks/reuse-matrix-air-20260919/README.md); the repair does not retroactively validate old runs.

## Trust-based reuse feasibility experiment

The separate [cross-customer reuse experiment](../benchmarks/WeavePort.Reuse/README.md) compares container replacement, fresh interpreters within a retained container and cooperative SDK-style reset. It includes alternating plugin modules, low-frequency customer traffic and explicit negative isolation controls. That experimental harness does not itself qualify the production scheduler. The development candidate now implements the explicit [approved-session reuse contract](reusable-plugins.md); actual-host measurements use the SDK mode below.

The current diagnostic recorder stores only populated latency buckets for each customer. It no longer allocates six dense 2,400-counter histograms for every registered test client before traffic. Run `dotnet benchmarks/WeavePort.Density/bin/Release/net10.0/WeavePort.Density.dll --histogram-check` after building to verify merged percentiles and sparse-customer allocation. Docker stages pin the resolved image ID in their configuration. These harness revisions require new source/image identities when comparing memory with older runs.

The current Docker observer additionally extracts generated instance names from the measured host’s owned CLI processes. This includes starting/pristine workers that no customer session exposes yet. It never treats all newly appearing Docker containers as owned. Run the built density harness with `--observer-check` for name-extraction controls; transient lifetimes may still fall between samples. Earlier production controls used session-exposed names only and may omit an unbound reserve from sampled container memory.

## Actual SDK reuse mode

The density executable accepts `ApprovedSdk: true` to use the native `$sdk.call` operation and an operator-approved profile. A provider must implement the `context` SDK function returning `{ tenant, configuration }`. `Arguments` may select an installed SDK fixture for trusted local execution; Docker uses the configured image. The bounded harness accepts up to 100,000 registrations and configures scheduler registration admission accordingly. These are harness guardrails, not proven capacity.

Use `Traffic: "population"` with explicit `CallsPerCustomerPerMinute`, a full period or longer, and seeded independent customer arrival phases. Worker count can remain null: `MemoryBudgetMiB` divided by the per-worker reservation constrains admission. Report per-customer success, intended-arrival latency, cleanup, worker starts/reuse, host RSS and separate container working-set/raw cgroup memory. A passing offered rate establishes one measured operating point, not the maximum. Source identity and host background load must accompany comparisons with the earlier experimental reuse harness.

For a separate warm operating-point measurement, set `WarmupCustomers` explicitly. Those calls run concurrently before the measurement and their duration is reported as `warmupSeconds`; they are not part of the measured per-customer arrival count. Keep the zero-warmup cold run and its failures. With two calls per customer, the per-customer p99 is effectively the worse call; report its acceptance separately from aggregate p99.

The [actual-host candidate evidence](../reports/verification/approved-session-reuse-20260920/README.md) retains source/package/Docker checks, cold and warm population runs, container memory, and the failed per-customer cold-start latency target.
