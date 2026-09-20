# Reuse security and many-customer capacity — MacBook Air

Measured on 2026-09-19: Apple M4 Air, 10 cores, 32 GiB RAM, Docker Desktop ~24 GiB. Other project services stayed running; their sampled raw Docker memory was about 11.4 GiB before the expanded I/O probes, so the entire 24-GiB VM was not free for this test. This is a source-built, uncommitted candidate; not release qualification and not comparable to the earlier M2 Ultra benchmark host. Primary image: `sha256:7ca4151f0fcc8116e66b48e9af6c14c1a8100da33a046351c8158c3b34b9fb70`.

## What these numbers mean

The reuse prototype executes two small, preinstalled Python plugin modules through local Docker stdio. Each one-call case uses a distinct synthetic customer identity per invocation and validates tenant, plugin and returned value. It does not provision a production customer per identity or include the production .NET protocol, host authorization callbacks, ingress, database or real customer dependencies. Grouped cases count a customer only after all three/ten correct calls. The production-host population controls are separate below.

`trusted` retains an interpreter and relies on cooperation; it demonstrably does not isolate arbitrary customer heap state. `process` starts a fresh interpreter in the container. `forkserver` uses a pristine customer-independent template and a new unprivileged child per call, with external cleanup checks. `fresh` replaces the entire container after each call. Unreviewed artifacts must retain stronger customer separation; these tests do not certify a hostile-code sandbox.

## Final pool scaling

One CPU quota per container; 128 MiB ceiling each, except 64-container cases use 64 MiB. Default payload 64 bytes. One invocation per container. Preparation is excluded from throughput and retained separately; fresh-container retirement is included. Twenty-second saturated trials are peaks, not sustainable offered-rate claims.

| Mode | Containers | Customers/s | p99 ms | Container peak MiB |
| --- | --- | --- | --- | --- |
| trusted | 1 | 1,685.5 | 0.67 | 12.39 |
| forkserver | 1 | 285.1 | 5.01 | 33.00 |
| trusted | 8 | 7,567.8 | 1.93 | 96.59 |
| forkserver | 8 | 956.9 | 13.29 | 246.86 |
| trusted | 16 | 8,152.5 | 5.54 | 182.84 |
| forkserver | 16 | 971.6 | 36.31 | 482.63 |
| trusted | 32 | 7,925.1 | 14.54 | 355.27 |
| forkserver | 32 | 986.6 | 128.48 | 954.01 |
| trusted | 64 | 7,244.8 | 36.67 | 702.39 |
| forkserver | 64 | 897.5 | 311.48 | 1,900.97 |
| process | 1 | 35.2 | 38.54 | 19.09 |
| process | 8 | 158.3 | 68.64 | 127.22 |
| fresh | 1 | 6.9 | 127.21 | transient / undersampled |
| fresh | 4 | 18.7 | 191.29 | transient / undersampled |

Actual container memory is sampled cgroup working set, not the configured memory ceiling, whole Docker VM footprint or macOS RSS. Short-lived fresh containers are undersampled. More containers do not imply more tiny-call throughput; I/O demand has a different curve. Initial scaling stops at 64 containers/4 GiB; later I/O probes expand to 128 containers/8 GiB only after a Docker headroom check and 2-GiB margin. Neither is a production worker cap.

## Arrival-rate boundary and repeated trials

Poisson arrivals, independent of completion, 16 containers, two 60-second seeds per point. Qualification requires all calls correct, no drops, overall and sufficiently populated time-window p99 ≤1 second, and bounded backlog growth. Last-third mean backlog may exceed first-third mean by at most max(two calls/worker, 100 ms of offered work). This is an explicit finite engineering criterion, not proof of unlimited stability. The highest passing and first rejected tested rates are:

```json
{
  "trusted": {
    "qualifiedLow": 6000,
    "firstRejected": 6375,
    "scope": "Two finite trials per point under the stated latency/backlog contract; not a universal server maximum"
  },
  "forkserver": {
    "qualifiedLow": 775,
    "firstRejected": 837,
    "scope": "Two finite trials per point under the stated latency/backlog contract; not a universal server maximum"
  }
}
```

| Mode | Offered/s | Repeat | Correct customers/s | p99 ms | Drops | Qualified |
| --- | --- | --- | --- | --- | --- | --- |
| trusted | 6000 | 1 | 5,999.0 | 10.68 | 0 | True |
| trusted | 6000 | 2 | 5,989.7 | 77.35 | 0 | True |
| trusted | 9000 | 1 | 6,508.4 | 1,007.75 | 143496 | False |
| trusted | 9000 | 2 | 6,501.8 | 1,007.75 | 143136 | False |
| trusted | 7500 | 1 | 6,469.7 | 1,007.75 | 55569 | False |
| trusted | 7500 | 2 | 6,407.1 | 1,007.75 | 58716 | False |
| trusted | 6750 | 1 | 6,544.9 | 1,007.75 | 6084 | False |
| trusted | 6750 | 2 | 6,468.8 | 1,007.75 | 9917 | False |
| trusted | 6375 | 1 | 6,374.0 | 260.40 | 0 | True |
| trusted | 6375 | 2 | 6,338.0 | 497.21 | 0 | False |
| forkserver | 650 | 1 | 646.7 | 21.64 | 0 | True |
| forkserver | 650 | 2 | 647.7 | 25.38 | 0 | True |
| forkserver | 1150 | 1 | 841.7 | 1,038.29 | 17537 | False |
| forkserver | 1150 | 2 | 836.2 | 1,038.29 | 17549 | False |
| forkserver | 900 | 1 | 831.6 | 1,038.29 | 3167 | False |
| forkserver | 900 | 2 | 831.9 | 1,038.29 | 3042 | False |
| forkserver | 775 | 1 | 772.7 | 199.05 | 0 | True |
| forkserver | 775 | 2 | 770.2 | 560.27 | 0 | True |
| forkserver | 837 | 1 | 820.0 | 1,028.01 | 147 | False |
| forkserver | 837 | 2 | 826.2 | 507.20 | 0 | True |

The resource observer streams its history to disk and retains scalar peaks/counts only. The measuring process uses substantially less than a whole CPU core in inspected trusted boundary trials; retained generator-lag histograms expose its own delays. Results still describe this complete experimental path on a shared laptop, not a hardware-independent plugin maximum.

## Thirty-minute operating-point validation

Each mode runs below its passing boundary, with a separate seed. There are no per-customer reserved containers. Every invocation uses a different customer identity within its run.

| Mode | Duration | Offered/s | Correct customers/s | Correct distinct customers | p99 ms | Peak MiB | Errors / drops |
| --- | --- | --- | --- | --- | --- | --- | --- |
| trusted | 30 min | 4500 | 4,499.43 | 8098973 | 7.10 | 191.03 | 0 / 0 |
| forkserver | 30 min | 550 | 550.05 | 990096 | 16.22 | 478.07 | 0 / 0 |

| Mode | Container median after warmup → end MiB | Measuring-host median MiB | Starts / final retirements | Backlog/latency qualified |
| --- | --- | --- | --- | --- |
| trusted | 181.97 → 189.05 | 33.98 → 29.31 | 16 / 16 | True |
| forkserver | 456.16 → 456.17 | 30.16 → 26.45 | 16 / 16 | True |

Memory comparison excludes the first five minutes and compares the first/last up-to-100 steady-state resource samples. Final retirements are normal pool shutdown. Live samples, queue windows, hashes and cleanup are retained; finite memory observations are not a general proof against leaks. The cooperative pool median increases by about 7.1 MiB over this comparison, while the fork-server median is effectively unchanged.

## Few calls per customer, serial same-plugin execution

The p99 values below are invocation latency, not full multi-call customer-workflow latency. Same customer/plugin jobs cannot occupy multiple containers concurrently; waiting jobs do not consume a lane. Two modules permit fan-out across different plugins; one-module cases prohibit that intra-customer fan-out. Earlier unconstrained grouped pilot cases and the interrupted undersized-group-queue probe remain historical evidence. Saturated grouping now queues up to two complete customer groups per worker (bounded by 10,000 calls), avoiding artificial idle lanes when one customer has ten sequential calls.

| Mode | Calls/customer | Plugins/customer | Arrival | Customers/s | Calls/s | p99 ms | Drops |
| --- | --- | --- | --- | --- | --- | --- | --- |
| trusted | 3 | 2 | saturated | 2,383.6 | 7,150.8 | 13.69 | 0 |
| trusted | 10 | 2 | saturated | 675.2 | 6,752.4 | 48.45 | 0 |
| forkserver | 3 | 2 | saturated | 287.4 | 862.1 | 111.77 | 0 |
| forkserver | 10 | 2 | saturated | 77.1 | 771.4 | 403.45 | 0 |
| process | 3 | 2 | saturated | 38.3 | 116.2 | 801.61 | 19 |
| process | 10 | 2 | saturated | 0.2 | 118.9 | 987.90 | 2400 |
| trusted | 3 | 2 | poisson | 1,670.3 | 5,010.9 | 240.48 | 0 |
| forkserver | 3 | 2 | poisson | 197.7 | 593.0 | 22.08 | 0 |
| trusted | 3 | 1 | saturated | 1,854.2 | 5,562.6 | 18.46 | 0 |
| trusted | 10 | 1 | saturated | 554.7 | 5,546.8 | 62.14 | 0 |
| forkserver | 3 | 1 | saturated | 265.9 | 797.7 | 122.24 | 0 |
| forkserver | 10 | 1 | saturated | 77.6 | 776.0 | 428.27 | 0 |
| process | 3 | 1 | saturated | 42.6 | 127.9 | 718.50 | 0 |
| process | 10 | 1 | saturated | 0.2 | 127.2 | 1,059.16 | 2452 |

## Workloads and injected faults

CPU=10 ms CPU time; I/O=100 ms simulated wait; memory=32 MiB touched allocation; payload=256 KiB; blend=1% 500-ms I/O, 9% 10-ms CPU, remainder short calls. Each case lasts 30 seconds. Burst rows offer a complete second of short calls at once. These are workload models, not measurements of a database or network service.

| Mode | Containers | Workload | Customers/s | p99 ms | Peak MiB |
| --- | --- | --- | --- | --- | --- |
| trusted | 8 | cpu | 701.1 | 14.25 | 92.74 |
| trusted | 8 | io | 75.2 | 114.02 | 97.29 |
| trusted | 8 | memory | 1,139.0 | 11.91 | 275.71 |
| trusted | 8 | payload 256 KiB | 441.0 | 34.89 | 104.00 |
| trusted | 8 | blend | 1,216.3 | 502.18 | 92.71 |
| trusted | 64 | io | 573.5 | 122.24 | 700.41 |
| forkserver | 8 | cpu | 417.5 | 27.48 | 252.77 |
| forkserver | 8 | io | 65.9 | 132.37 | 257.73 |
| forkserver | 8 | memory | 616.8 | 19.79 | 340.36 |
| forkserver | 8 | payload 256 KiB | 292.7 | 46.56 | 266.04 |
| forkserver | 8 | blend | 590.6 | 507.20 | 252.08 |
| forkserver | 64 | io | 578.7 | 125.95 | 2,010.54 |
| trusted | 128 | io | 1,227.9 | 108.48 | 1,389.18 |
| forkserver | 128 | io | 767.0 | 361.62 | 3,910.75 |
| trusted | 16 | burst 3000/s | 2,999.7 | 507.20 | 181.52 |
| forkserver | 16 | burst 600/s | 599.9 | 600.68 | 479.20 |

Closed-loop fault cases last 90 seconds; additional independent Poisson-arrival cases last 60 seconds at 250/500 offered calls per second. They inject a hang/crash/cleanup failure every hundred calls across four containers, and require retirement before reuse. Closed-loop latency alone does not show the waiting experienced by independent arrivals when all lanes block. Fault containment counts only actually executed faults; admission drops are separately reported. Intentional failed customers are not included in successful customer throughput. A fault campaign is not a zero-error service-capacity point.

| Mode | Arrival | Offered/s | Drops | Correct customers/s | Expected faults contained | Unexpected errors | Normal echo p99 ms | Container starts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| trusted | saturated | — | 0 | 380.8 | 356/356 | 0 | 81.29 | 357 |
| process | saturated | — | 0 | 87.0 | 80/80 | 0 | 121.03 | 83 |
| forkserver | saturated | — | 0 | 291.3 | 269/269 | 0 | 146.22 | 271 |
| trusted | poisson | 250 | 0 | 236.1 | 149/149 | 0 | 15.13 | 150 |
| trusted | poisson | 500 | 10367 | 302.5 | 229/229 | 0 | 1,007.75 | 229 |
| forkserver | poisson | 250 | 0 | 240.0 | 149/149 | 0 | 121.03 | 151 |
| forkserver | poisson | 500 | 12935 | 265.6 | 198/198 | 0 | 1,124.32 | 199 |

## Security findings and corrections

Final expected-behavior checks: Engine 195/195; CLI bookend 195/195. Deliberate negative controls are included in these passes.

1. **Heap state can cross customers in the cooperative mode.** Five seeded patterns—module global, mutable default argument, cache, context variable, logger—remain readable by the next customer. The seed/probe is deliberate, but the retention pattern can be an ordinary author mistake. A generic SDK reset is not a confidentiality boundary.
2. **Process exit did not remove kernel IPC state.** A later customer read the previous customer's canary from System V shared memory in both fresh-interpreter and fork-server modes. External audits of System V shared memory/semaphores/message queues and POSIX queues now mark any residue non-reusable. The exact container is destroyed; the next customer cannot access it. Both transports explicitly create a private IPC namespace. In trusted mode the audit shares the plugin interpreter and can itself be subverted; only the fork-server design places it in the separate privilege-separated supervisor. Process-keyring creation was denied under the tested Docker policy.
3. **Explicit hostile probes are distinct from accidental retention.** Tests attempt forged stdout/results, oversized frames, pickle execution, parent access/signals and access to the private template socket. Fork child results are bounded JSON, not deserialized pickle. Children have UID/GID 65532 and no effective capabilities; supervisor capabilities are limited to SETUID/SETGID/KILL. No host mounts, network or Docker socket enter a plugin. Local grant mutation intentionally succeeds: the local scope is a stub, so real authorization must remain in the host.
4. **Timeout handling had a false failure.** A separate 100-ms post-response exit wait killed four otherwise correct results at 64-way saturation. A controlled 250-ms exit reproduced it. Response and process exit now share a 1.5-second deadline; 250 ms succeeds, 2 seconds retires, and the corrected 64-way repeat has zero unexpected errors. Cleanup that cannot safely remove child-owned files retires the container; privileges were not broadened to conceal this condition.

The two public fixture modules also do not qualify confidentiality between private customer code bundles: readable files in a shared image are shared with its plugins. Customer secrets and private artifacts must not be baked into that bundle.

The production scheduler's existing prohibition on transferring used workers between customers is unchanged. The kernel leakage finding concerns the reuse experiment. Finite controls do not establish protection against arbitrary native code, kernel vulnerabilities or side channels. See [method and source](../../../benchmarks/WeavePort.Reuse/MATRIX.md) and [author guidance](../../../benchmarks/WeavePort.Reuse/AUTHOR-GUIDE.md).

## Actual host and other languages

The following source-built .NET scheduler tests use Python/TypeScript/C# Docker workers, seeded customers with one call/minute, no warmup, a pristine reserve ceiling of two slots and a 4-GiB shared reservation. Used workers remain customer-bound. Each language's staircase stops at its first failure. Every customer must receive a result within the configured one-second criterion; this is stricter than checking only global p99. The fixture calls `context` and validates the customer identity plus 64-byte configuration. Production-control containers use 0.5 CPU and 64 MiB each, unlike the one-CPU reuse trials. Registration is measured separately before traffic. The diagnostic allows a five-second admission wait and ten-second execution deadline so overload remains observable; these are not one-second production timeout recommendations.

| Language | Candidate | Customers | Offered calls/s | Correct calls | Observed calls/s | p99 ms | Peak workers | Container MiB | Passed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| python | before reserve fix | 100 | 1.67 | 94 | 1.57 | 435.91 | 64 | 703.27 | False |
| python | after reserve fix | 100 | 1.67 | 100 | 1.66 | 427.32 | 64 | 704.70 | True |
| python | after reserve fix | 500 | 8.33 | 498 | 7.65 | 5,192.96 | 64 | 692.12 | False |
| typescript | before reserve fix | 100 | 1.67 | 94 | 1.56 | 427.32 | 64 | 2,130.20 | False |
| typescript | after reserve fix | 100 | 1.67 | 100 | 1.66 | 435.91 | 64 | 2,130.00 | True |
| typescript | after reserve fix | 500 | 8.33 | 453 | 6.95 | 5,297.34 | 64 | 2,070.97 | False |
| csharp | before reserve fix | 100 | 1.67 | 93 | 1.55 | 360.82 | 64 | 544.43 | False |
| csharp | after reserve fix | 100 | 1.67 | 100 | 1.66 | 1,341.86 | 64 | 513.24 | False |

The initial 100-customer rows contain 6/7 spurious busy responses, not a real throughput boundary. A deterministic startup-gate regression reproduced the reserve/foreground race. Foreground acquisition now waits for a pending shared candidate, rechecks readiness after launch-slot acquisition and takes priority over refill. The corrected rows rerun the same arrival seed. The corrected observer also includes unbound pristine/startup instance names from owned CLI processes; earlier memory samples could omit these. At 500 Python/TypeScript customers the remaining busy results spend about five seconds in the queue, matching admission expiry under overload. C# returns all 100 results correctly but misses the one-second latency criterion in this control. These measurements expose production startup/eviction/protocol costs. They must not be substituted with the much higher reuse-prototype rates.

### Repeated low-frequency customers and memory budget

Python controls below span two minutes: each registered customer offers two calls, one per minute. Two arrival seeds are tested per population unless a failure stops the staircase. Admission budgets of 4/8 GiB imply up to 64/128 workers for the configured 64-MiB profile; no separate worker-count cap is supplied. The same customer can appear in both minutes, so observed calls/s is not a rate of newly provisioned customers. Cold dispatch counts expose scheduler-classified startup/reattachment; only passing rows allow the remaining successful calls to be interpreted as resident reuse. Registration alone does not reserve a container. Container memory is separate from host/CLI RSS; a 6-GiB host/CLI guard protects this probe. A preflight additionally reserves 2 GiB beyond measured background Docker usage.

| Budget MiB | Confirmed customers (all trials) | Offered calls/s | First rejected customers | Rejected-stage failed calls |
| --- | --- | --- | --- | --- |
| 4096 | 128 | 2.13 | 256 | 0 |
| 8192 | 128 | 2.13 | 256 | 0 |

These are tested population brackets at one call/customer/minute, not a universal account limit. Qualification uses every available repeat, with at least two seeds; the 256-customer follow-ups below are included. An observer guard would invalidate a stage rather than establish a capacity boundary; its reason is retained in the JSON. The detailed repetitions follow.

| Budget MiB | Customers | Seed | Offered calls/s | Correct calls | Cold dispatches | Observed calls/s | p99 ms | Peak workers | Container MiB | Passed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 4096 | 64 | 1729 | 1.07 | 128 | 64 | 1.07 | 267.70 | 64 | 706.03 | True |
| 4096 | 64 | 1730 | 1.07 | 128 | 64 | 1.07 | 262.42 | 64 | 706.11 | True |
| 4096 | 128 | 1729 | 2.13 | 256 | 256 | 2.13 | 406.58 | 64 | 704.66 | True |
| 4096 | 128 | 1730 | 2.13 | 256 | 256 | 2.13 | 458.14 | 64 | 703.59 | True |
| 4096 | 256 | 1729 | 4.27 | 512 | 512 | 4.25 | 675.36 | 64 | 703.80 | True |
| 4096 | 256 | 1730 | 4.27 | 512 | 512 | 4.26 | 746.01 | 64 | 703.59 | True |
| 4096 | 512 | 1729 | 8.53 | 944 | 944 | 7.54 | 5,403.81 | 64 | 703.73 | False |
| 8192 | 64 | 1729 | 1.07 | 128 | 64 | 1.07 | 230.58 | 65 | 716.32 | True |
| 8192 | 64 | 1730 | 1.07 | 128 | 64 | 1.07 | 237.57 | 65 | 715.58 | True |
| 8192 | 128 | 1729 | 2.13 | 256 | 128 | 2.13 | 259.83 | 128 | 1,406.02 | True |
| 8192 | 128 | 1730 | 2.13 | 256 | 128 | 2.13 | 284.17 | 128 | 1,405.43 | True |
| 8192 | 256 | 1729 | 4.27 | 512 | 512 | 4.25 | 761.01 | 128 | 1,403.80 | True |
| 8192 | 256 | 1730 | 4.27 | 512 | 512 | 4.26 | 1,056.81 | 128 | 1,401.96 | False |

The 8-GiB, 256-customer second seed returned every call correctly but crossed the one-second criterion. Four additional 120-second controls use new seeds and counterbalanced budget order (8/4 GiB, then 4/8 GiB). Every per-customer p99 must pass; with two calls/customer that tests the slower call, so a passing aggregate p99 alone is insufficient. An occasional over-limit result is retained and prevents qualifying that population across all repeats. At 256 customers each budget passes three of four runs; all requests return correctly, but one run per budget misses latency. The conservative confirmed population is therefore 128 for both budgets under this finite protocol, not 256. These controls do not establish a monotonic capacity gain from additional memory.

| Order | Budget MiB | Seed | Correct calls | p99 ms | Maximum ms | Passed |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 8192 | 1731 | 512 | 649.00 | 922.57 | True |
| 2 | 4096 | 1731 | 512 | 564.61 | 683.57 | True |
| 3 | 4096 | 1732 | 512 | 2,774.40 | 3,204.29 | False |
| 4 | 8192 | 1732 | 512 | 768.62 | 841.59 | True |

## Improvements retained in the candidate

- Lifecycle-maintained active/resident indexes replace dormant-registry scans in dispatch. With four warmed native C# workers, 10,000 registrations improve from ~6,486 to ~99,010 calls/s on the final candidate. At 100,000 registrations: ~101,380 calls/s for **four hot customers**, ~308.46 MiB incremental managed registration memory, ~0.191-second disposal. The memory figure is the difference in GC-reported live managed bytes after forced collections, not total process/container memory. This diagnostic is not 100,000 distinct customers/s. Both source and locally packed scheduler consumers pass 88 assertions; packed hosting regressions pass with the same Hosting DLL SHA.
- Immutable Python bytecode precompilation and approved standard-library template preload remove repeated compilation/import cost. Customer modules/data never enter the template.
- Direct Engine attach removes one CLI helper per retained container. Matched reversed-order runs retain CLI/Engine comparisons. Summed CLI RSS can include shared pages and is not a physical-memory saving guarantee.
- Sparse per-customer benchmark histograms replace six preallocated arrays: 1,000 two-sample records allocate 1,264,176 bytes in the verified control, rather than roughly 115 MB of initially empty arrays.

## Evidence and reproduction

[Manifest](manifest.json) hashes compact retained data. Capacity/soak windows are retained as compressed JSON Lines; production observer histories are compressed JSON (`supervisor.json.gz`). Identities redact unrelated container names while preserving their count, machine settings, image identity and source hashes. Original overloads, the 64-way false timeout, deliberate leaks and the CLI CapAdd normalization false failure are retained rather than discarded. The latter was a test representation mismatch (`CAP_` prefix), not a capability change.

For a fresh checkout, prepare images and the source-built density harness before timing, then copy the retained configurations into a new artifact root:

```sh
env -u WEAVEPORT_PACKAGE_SET dotnet build benchmarks/WeavePort.Density -c Release
env -u WEAVEPORT_PACKAGE_SET dotnet build benchmarks/WeavePort.Registry -c Release
docker build -t weaveport-poc-python:1 -f plugins/python/Dockerfile .
docker build -t weaveport-poc-typescript:1 -f plugins/typescript/Dockerfile .
docker build -t weaveport-poc-csharp:1 -f plugins/csharp/Dockerfile .
docker build -t weaveport-reuse-matrix:1 -f benchmarks/WeavePort.Reuse/Matrix.Dockerfile benchmarks/WeavePort.Reuse
mkdir -p artifacts/reuse-matrix
cp -R reports/benchmarks/reuse-matrix-air-20260919/configs artifacts/reuse-matrix/configs
python3 tools/performance/matrix_security.py artifacts/reuse-matrix/security-exit-fixed --engine
python3 tools/performance/reuse_matrix.py artifacts/reuse-matrix/configs/canonical-scaling.json artifacts/reuse-matrix/canonical-final
python3 reports/benchmarks/reuse-matrix-air-20260919/reproduce/run-final-phases.py
python3 reports/benchmarks/reuse-matrix-air-20260919/reproduce/run-production-repeat.py
python3 reports/benchmarks/reuse-matrix-air-20260919/reproduce/run-production-confirmation.py
python3 tools/performance/reuse_matrix.py artifacts/reuse-matrix/configs/faults-arrivals-final.json artifacts/reuse-matrix/faults-arrivals-final
python3 tools/performance/matrix_security.py artifacts/reuse-matrix/security-bookend-cli
for count in 4 10000 100000; do
  dotnet benchmarks/WeavePort.Registry/bin/Release/net10.0/WeavePort.Registry.dll "$count" "artifacts/reuse-matrix/registry-final/$count"
done
```

These commands are for empty output paths; do not overwrite a prior run. They reproduce the methods against the current candidate. The sequential script retains the original names `production-*` and `soaks-final`; in a fresh run those contain current-code results. Original before-fix measurements and interrupted phases are historical evidence, not regenerated by running corrected code. The report writer requires the retained before/after evidence layout, so it is not a generic renderer for an arbitrary fresh campaign. Use the [matrix commands](../../../benchmarks/WeavePort.Reuse/MATRIX.md), [configurations](configs/canonical-scaling.json) and [sequential run script](reproduce/run-final-phases.py) with fresh artifact directories; never run benchmarks alongside builds or other load tests. The script's known brackets are observations for this machine and must be adjusted when moving machines. Runtime/source/package identities distinguish all series. `scripts/verify.sh` qualification of committed HEAD, publication and production cross-customer reuse rollout were not performed.
