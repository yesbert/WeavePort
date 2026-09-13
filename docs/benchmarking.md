# Current product benchmarks

Run one complete suite against the current delivered core:

```sh
./scripts/benchmark.sh --distribution /absolute/path/to/WeavePort/0.1.0-internal.2/payload
```

An extracted bundle root also works. The command verifies the core package hashes against the retained release manifest, copies them into the local feed, builds the optional gateway and SDK fixtures, checks loaded DLLs against package contents, runs correctness checks, then measures the suite. It does not rebuild the released core or change Docker/services. Existing generated feed contents are moved into the run directory before replacement.

Use the pinned .NET SDK, Python and Node, with enough local space for generated BenchmarkDotNet builds. Package restore/build may download development dependencies. This is not an offline benchmark bootstrap; the installed application's normal execution remains offline.

## Workloads

| Measurement | Combinations | Scope |
|---|---|---|
| Warm SDK | Three languages × embedded/gateway × tiny/callback/64 KiB/8 MiB | Complete caller result and validation; host/bind/startup outside timing |
| Cold lifecycle | Three languages × embedded/gateway | Create host/gateway, bind, start, perform callback, validate, dispose; includes fixture provenance recording |
| Request/resource runs | Three languages × two topologies × callback/64 KiB × two repeats | 16 independent tenant clients, three-second bounded load, complete result checks and sampled resources |

The stream sizes are text payload sizes: 8 or 1,024 rows of 8,192 characters plus JSON/envelope overhead. The gateway uses local HTTP/2 loopback and an additional worker-host process. Both execution paths launch the same explicitly trusted native providers.

## Interpretation

BenchmarkDotNet 0.15.8 uses one launch, three warmup and six measured warm iterations with a target iteration time of 250 ms; cold uses two warmup and six single-invocation measured iterations. These are bounded baseline measurements. Read dispersion and warnings; do not treat them as a high-confidence production SLO or compare historical runs with different workloads/runtime settings as causal optimization evidence.

Warm timing includes result validation. Request latency is recorded immediately after the complete response and before validation; throughput/duration includes the validation work. Request percentiles use approximately 1% logarithmic histogram buckets and are distinct from BenchmarkDotNet iteration statistics. Every configured client must serve requests without errors.

Managed allocations cover the measured host, not Python/TypeScript or gateway memory. Request runs capture sampled root-process RSS for coordinator, gateway and workers at approximately 100 ms intervals; sums may double-count shared pages, miss peaks and exclude escaped descendants. The allocation counter stops after load/drain and before report analysis. These are local closed-loop workloads, not maximum capacity, multi-machine scaling or a hostile-plugin sandbox qualification.

## Evidence

Raw output lives under `artifacts/performance/<run>/`. Retain compact successful results under [current benchmarks](../reports/benchmarks/current/README.md), including package/runtime/fixture hashes, sample counts, mean/dispersion, per-client request outcomes and resource scope. Build failures, unsuccessful benchmarks and changed identities produce nonzero exit status. Failed raw runs remain available for diagnosis and must not be reported as completed measurements.

The [historical index](history.md) contains the previous PoC suites. Windows qualification remains explicitly open; these macOS measurements do not close it.

After a complete successful run, retain its audited compact report with:

```sh
python3 -B tools/performance/summarize.py artifacts/performance/<run> --output reports/benchmarks/current
```

The summarizer refuses missing cells, missing author SDK verification, changed fixture identities, failed clients and incomplete runs. The primary benchmark command deliberately leaves failed and raw output in the ignored artifacts tree for inspection.

For a multi-hour fault/recovery workload with early-stop controls and chronological resource evidence, see [supervised k6 soak testing](soak-testing.md). Optimization comparisons of source-built candidates are separate from the exact delivered release baseline.

The [runtime optimization comparison](../reports/optimization/runtime-and-soak/README.md) records source-built before/after measurements separately from the delivered baseline.
