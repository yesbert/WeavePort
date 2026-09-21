# Shared-worker comparison

Run the three-language shared host qualification first, then:

```sh
dotnet run --project benchmarks/WeavePort.Shared -c Release -- "$PWD"
```

The executable compares one resident process at degree one and sixteen, with 64 distinct tenant clients. It records separate controlled 150 ms asynchronous delay and 100,000-iteration SHA-256 PBKDF2 workloads. Delay demonstrates overlapping waits; it is not CPU performance. Python uses its bounded SDK thread pool for native PBKDF2. Node uses asynchronous crypto and the runtime's default libuv pool; degree sixteen does not imply sixteen CPU threads. C# executes native PBKDF2 through SDK dispatch.

Every result checks tenant identity; CPU results also verify the expected digest. Warmup and startup are excluded. Latencies include scheduler queue time. JSON evidence records throughput, p50/p95/p99, host allocations, sampled worker peak RSS, runtime/OS/architecture, source hashes and the actually loaded host assembly hash. RSS sampling occurs every 10 ms and can miss shorter peaks. Measurements are individual local runs, not a statistical confidence interval or a universal speed guarantee. Run on an otherwise representative deployment before choosing a degree.

Output goes to `reports/benchmarks/shared-<UTC timestamp>.json`. By default it is explicitly labelled source-build evidence. For release evidence use a published `UsePackedCore=true` executable, the installed author artifacts selected using the same `WP_SHARED_*` inputs as the test harness, and set `WP_BENCHMARK_QUALIFICATION` to the successful candidate qualification identifier. Do not label a source run as packed evidence.

The initial source experiments on 2026-09-21 showed roughly 6.6 versus 104–105 calls/s for the controlled delay at degree one/sixteen. Native CPU throughput varied by language/runtime and concurrent machine activity; see the raw reports rather than treating those measurements as a sizing prescription. No speculative optimization was applied based on these short runs: preserving callback ownership and cancellation accounting takes priority over unmeasured allocation changes.
