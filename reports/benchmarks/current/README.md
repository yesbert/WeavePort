# Current performance baseline — 20260913

Measured core: **0.1.0-internal.2**, exact qualified NuGet and installed author SDK bytes. Benchmark source: `6fa0cdc15466d49c54818eba6df72f42c9c365ca`. Run: `20260913-082237-9c397b2c`.

All 30 timing cells and 24 bounded request runs passed. The separate SDK correctness stage passed before timing. Optional Gateway was built from this source and is identified separately; it is outside the core distribution.

Environment: Apple M2 Ultra, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0], Arm64, SDK 10.0.401, .NET 10.0.12 (10.0.12, 10.0.1226.42308), Python 3.14.7, Node v26.8.2. Recorded Docker state: running. These are synthetic same-machine trusted-process and loopback measurements, not a capacity limit, SLO or qualification of Windows, remote networks or hostile plugins.

## Complete-result timing

Arithmetic means in milliseconds. Warm operations include complete result validation; cold includes harness setup, first callback, provenance hashing/writing and disposal. Six requested measured iterations per cell, one launch; BenchmarkDotNet may exclude outliers. [Machine-readable evidence](evidence.json) retains actual sample counts, distributions, standard deviations, confidence intervals and pre/post-exclusion measurements. Small samples and cold-start variability limit precision.

| Language | Topology | Tiny | Callback | 64 KiB | 8 MiB | Cold lifecycle |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| csharp | local | 0.042 | 0.077 | 0.266 | 35.323 | 98.472 |
| csharp | gateway | 0.190 | 0.232 | 0.690 | 57.467 | 372.097 |
| python | local | 0.094 | 0.166 | 0.677 | 60.435 | 90.067 |
| python | gateway | 0.236 | 0.364 | 1.243 | 82.756 | 377.645 |
| typescript | local | 0.044 | 0.077 | 0.330 | 38.524 | 76.340 |
| typescript | gateway | 0.181 | 0.173 | 0.862 | 59.772 | 361.666 |

## Individual requests and resources

Each cell uses 16 clients, one outstanding operation each, two separate three-second runs after warmup plus draining. Ranges below span the two runs. All clients completed requests without errors. p99 comes from request histograms with approximately 1% bucket resolution, not benchmark iteration statistics. Request latency excludes validation; throughput includes it.

| Language | Topology | Workload | Requests/s | Request p99 ms | Peak summed RSS MiB |
| --- | --- | --- | ---: | ---: | ---: |
| csharp | local | callback | 56150.94–63674.30 | 1.53–1.70 | 1098.39–1102.22 |
| csharp | local | list-64k | 9565.22–10071.48 | 3.03–3.32 | 1149.14–1150.88 |
| csharp | gateway | callback | 24745.65–29762.35 | 1.92–2.25 | 1218.23–1220.19 |
| csharp | gateway | list-64k | 5199.68–5226.74 | 5.62–5.79 | 1278.89–1279.02 |
| python | local | callback | 31333.59–33356.07 | 1.20–1.29 | 471.67–471.69 |
| python | local | list-64k | 9036.69–9362.30 | 2.56–2.69 | 493.89–494.64 |
| python | gateway | callback | 20191.66–20725.44 | 1.86–1.92 | 590.41–591.00 |
| python | gateway | list-64k | 5029.56–5079.20 | 4.89–5.09 | 622.30–625.70 |
| typescript | local | callback | 62336.48–64691.98 | 1.26–1.33 | 944.11–945.41 |
| typescript | local | list-64k | 9405.78–10415.17 | 2.75–8.13 | 1107.55–1164.94 |
| typescript | gateway | callback | 27279.05–30657.07 | 1.86–2.00 | 1044.50–1045.66 |
| typescript | gateway | list-64k | 5509.38–5558.25 | 4.65–4.99 | 1201.56–1203.00 |

RSS samples cover the measuring host, optional gateway and known worker root processes every 100 ms. Shared pages can be counted repeatedly; descendants, Docker VM and unrelated applications are excluded, and sampling can miss peaks. Managed allocations cover the measuring .NET process only. Raw per-client outcomes and resource samples are retained in evidence.json.

Reproduce with the [current benchmark command and workload definitions](../../../docs/benchmarking.md). Full logs and generated builds stay in ignored local artifacts. Earlier attempts failed project discovery or used a stale npm installation; none contribute to this baseline. No performance improvement over those historical runs is claimed.
