# Initial memory-led scheduling observation

Source-built candidate, 2026-09-19, Apple M4 MacBook Air (10 cores, 32 GiB), .NET 10.0.12, ARM64. Existing services remain active; Docker is allocated approximately 24 GiB. This is separate from historical M2 Ultra release evidence and is not qualification of published packages.

## Policy and workload

No independent worker-count ceiling. Shared reservation budget 12,288 MiB, Python-fixture reservation 64 MiB, at most eight simultaneous starts, 120-second idle retention, no pristine reserve. One independently phased call per customer per minute, 100 customers and one plugin per customer, no warmup. The native stage runs 120 seconds. The Docker stage requests 60 seconds but is invalidated by its observer. The short context-return fixture verifies each bound tenant and payload; it is not real business logic.

## Passing native observation

| Metric | Value |
| --- | ---: |
| Customers / resident workers | 100 / 100 |
| Successful calls / failed / dropped | 200 / 0 / 0 |
| Observed calls per second | 1.667 |
| End-to-end p99, including cold starts | 105.06 ms |
| Maximum end-to-end latency | 162.84 ms |
| Cold calls / evictions | 100 / 0 |
| Peak sampled worker process RSS | 1,164.75 MiB |
| Peak sampled host/load-generator RSS | 101.13 MiB |
| Peak simultaneous summed RSS | 1,247.02 MiB |
| Reserved worker memory at end | 6,400 MiB |
| Workers / bindings after cleanup | 0 / 0 |

All 100 returning-customer calls reuse resident workers. RSS samples can count shared pages repeatedly; neither the RSS peak nor its quotient per worker is an exclusive physical-memory guarantee. Peak columns can occur at different times. With two calls per customer, per-customer percentiles are descriptive observations, not statistically established SLOs. This proves growth beyond 32 and reuse across periods, not maximum customer capacity or maximum requests/second.

The budget currently accounts declared reservations, not automatically discovered free RAM or adaptive admission against measured RSS. Host, queue, Docker CLI and other-service memory require headroom outside that budget. Smaller reservations need workload-specific evidence; this fixture does not establish a safe reservation for arbitrary plugins.

## Invalid Docker observation

The Docker memory observer (`docker stats --no-stream`, 15-second timeout) failed and requested controlled shutdown. This stage is not a passing latency, density or capacity observation. Its partial counters remain in [invalid Docker evidence](docker-invalid.json); cleanup left no known owned containers. Earlier large-customer Docker attempts also failed in this observer. Fix and verify the sampler before increasing Docker population; do not infer capacity from these failures.

## Verification and next discussion

51 scheduler assertions pass both source and locally packed candidate consumers, including a 40-worker memory-led run with one concurrent launch and eviction of one idle worker at memory exhaustion. Existing hosting regressions, four observer unit controls and two harness negative controls pass. Documentation, OpenSpec and code style pass. Full committed-HEAD release qualification has not run. SDK multi-exchange streams remain unsupported by the new optional scheduler; direct-host stream behavior is unchanged.

Next compare declared reservation admission with a deliberately designed measured-memory policy, fix the Docker observer, then increase customers at fixed per-customer frequency. Refine the first reproducible SLO/resource boundary with repeated longer runs and mixed heavy/burst traffic. Do not equate a passing last stage with a discovered server maximum.

[Native evidence](native.json) includes per-customer results, configuration, cleanup and hashes. Machine-local config paths are omitted from the retained JSON; original raw-result hashes and source hashes identify each measured run.
