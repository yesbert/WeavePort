# Trust-based reuse: MacBook Air feasibility experiment

2026-09-19, Apple M4 MacBook Air, 10 CPU cores, 32 GiB physical memory, Docker Desktop with approximately 24 GiB allocated. Existing services remain running. Each fixture container has a 64 MiB memory ceiling and 0.5 CPU quota. This is an experimental Python supervisor and SDK-style scope, **not** a performance claim for the published SDK or the .NET host.

## One execution slot, alternating customers and plugins

Two repetitions, reversed variant order, two preinstalled compatible Python plugin modules, a 64-character payload and a distinct customer on each call. Each variant runs at least 200 calls and ten seconds. The container-replacement runs take longer because they must complete 200 full lifecycles. Reusable-container preparation is separately recorded and excluded from steady turnover throughput; removing every fresh container is included.

| Variant | Calls/second | Response p99 | Sampled container memory peak |
| --- | ---: | ---: | ---: |
| New container per customer call | 1.53–1.91 | 1,050–1,504 ms | 13.25–13.62 MiB |
| Fresh interpreter in retained container | 4.72–4.81 | 306–363 ms | 24.17–24.25 MiB |
| Retained interpreter, cooperative reset | 1,449.5–1,456.1 | 1.20–1.64 ms | 13.19–13.30 MiB |

All recorded outputs match the actual customer, selected plugin and expected result. The two trusted runs contain 14,496 and 14,561 distinct customers respectively. SDK-style cleanup averages 0.052–0.053 ms in these saturated runs. Process replacement includes full Python interpreter and prototype imports under the CPU quota; this is not the minimum possible OS process-creation cost. Different languages, dependencies and reset work remain unmeasured.

## Same low-frequency arrival schedule

500 customers, one call each over 60 seconds, four sequential execution slots, identical seeded arrivals for all variants. Container preparation is separately recorded. These four slots are a comparison control, not a production worker limit. End-to-end latency starts at intended arrival and includes queue waiting. This small fixture contains no external database or production callback work.

| Variant | Correct calls | Time including drain | Response p99 | Sampled container memory peak |
| --- | ---: | ---: | ---: | ---: |
| New container per customer call | 500/500 | 120.78 s | 60,574 ms | 50.76 MiB |
| Fresh interpreter in retained container | 500/500 | 60.00 s | 753 ms | 94.48 MiB |
| Retained interpreter, cooperative reset | 500/500 | 60.00 s | 7.00 ms | 51.21 MiB |

The offered load is 8.33 calls/second. Fresh-container throughput cannot sustain it with these four slots and its queue grows. The replay deliberately drains the bounded offered cohort rather than expiring queued calls; a production admission deadline would reject earlier. `passed` in this experiment means correct results and cleanup; the separate `meetsOneSecondP99` field is false for the fresh-container runs. Lane assignment is round-robin with per-lane FIFO, not the full production fair scheduler. A short documentation/observer validation ran during the first fresh-container population stage; the machine is not an isolated benchmark host. Treat magnitudes and repeat ranges as observations, not precise universal ratios.

The retained-interpreter population case additionally measured approximately 121 MiB peak macOS child/helper RSS and 26 MiB measurement-host RSS. These must remain separate from Docker container memory: RSS includes shared pages and the CLI/observer helpers. Sampling can miss transient peaks. Full calls and resource logs remain under the local `artifacts/reuse` directories.

## Additional 5,000-customer probe

Four retained interpreters serve 5,000 distinct synthetic customers, one call each over 60 seconds, with 5,000 correct results and no failures. Offered load is 83.33 calls/second; achieved throughput is 83.32 calls/second, response p99 5.49 ms and maximum 15.42 ms. Sampled container memory peaks at 52.49 MiB, macOS helper RSS at 123.02 MiB and measuring-host RSS at 39.83 MiB. All four containers are removed after measurement. This is one run with two small preinstalled modules and simulated identities; it does not measure a production registry of 5,000 configured customers or establish maximum capacity.

## Isolation controls and authors

The initial and final fixture each pass 42 expected-behavior checks (14 per variant): alternating customer/plugin results, managed files/environment/children, cooperative grant/expiry checks, writable-directory cleanup, failed reset, unregistered threads, escaped child process groups, crash, hang and a hidden-global negative control. Poisoned slots are replaced before another customer executes; all explicitly owned containers are removed after the tests.

**The trusted hidden-global control reproduces a cross-customer data leak.** A successful cooperative reset cannot detect all in-process state. The fresh interpreter and fresh container prevent that specific global-state carryover; this is not a proof against arbitrary hostile code. The permission fixture is a local cooperative stub, not the real host authorization boundary. Same-UID child replacement also does not create a complete hostile-code boundary within a reused container.

The [author guide](../../../benchmarks/WeavePort.Reuse/AUTHOR-GUIDE.md) and executable examples document invocation-owned state, resource registration, cancellation, callback expiry and failed cleanup. Authors following examples reduce defects; examples do not establish an isolation guarantee. The published SDK and production no-cross-customer-reuse contract remain unchanged.

## Evidence and interpretation

Per-run JSON retains outcomes, quantiles, cleanup confirmation, machine/image identity and source hashes. The final fixture adds a host-execution guard; it refuses to run destructive fixture cleanup outside its disposable image. Image identities distinguish that guard revision from earlier measured runs. No plugin receives a host mount, network access or Docker socket.

The experiment supports further development of explicitly trusted cooperative reuse. It does not establish a maximum customer count, long-term memory stability, real-plugin dependency compatibility or production security. A production implementation needs host-owned trust/artifact policy, external authority validation, compatibility pool keys, cleanup-failure retirement and additional languages/workloads. Keep unreviewed plugins isolated between customers.
