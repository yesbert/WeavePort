# k6 soak result

Status: **passed**; stop reason: **completed**.

Elapsed: 14405.5 seconds. Source: `b0d1c52066d4502368c50e5a2ac9c36052d41139`. See result.json for exact artifacts and configuration.

Native trusted stdio providers, loopback gRPC; sampled owned process-group RSS includes k6, shared pages may repeat; no sandbox or capacity qualification.

| Metric | Value |
| --- | ---: |
| completed_operations count | 7852212.000 |
| completed_operations rate | 545.26182107 |
| unexpected_errors rate | 0.00000000 |
| unexpected error samples | 0 |
| expected_faults count | 387772.000 |
| expected_faults rate | 26.92709607 |
| operation_ms avg | 12.811 |
| operation_ms p(95) | 88.000 |
| operation_ms p(99) | 173.000 |
| operation_ms max | 4845.000 |

Resource samples: 2823; peak summed RSS: 1916.0 MiB.

| Tenant | Completed | Unexpected error rate | p99 ms |
| --- | ---: | ---: | ---: |
| tenant-0 | 326385 | 0.00000000 | 168.00 |
| tenant-1 | 318472 | 0.00000000 | 203.00 |
| tenant-2 | 337074 | 0.00000000 | 160.00 |
| tenant-3 | 326487 | 0.00000000 | 168.00 |
| tenant-4 | 319116 | 0.00000000 | 204.00 |
| tenant-5 | 337029 | 0.00000000 | 158.00 |
| tenant-6 | 325292 | 0.00000000 | 167.00 |
| tenant-7 | 317058 | 0.00000000 | 203.00 |
| tenant-8 | 336738 | 0.00000000 | 160.00 |
| tenant-9 | 326271 | 0.00000000 | 168.00 |
| tenant-10 | 318367 | 0.00000000 | 203.00 |
| tenant-11 | 335687 | 0.00000000 | 159.00 |
| tenant-12 | 325726 | 0.00000000 | 167.00 |
| tenant-13 | 318133 | 0.00000000 | 203.00 |
| tenant-14 | 336930 | 0.00000000 | 160.00 |
| tenant-15 | 326646 | 0.00000000 | 167.00 |
| tenant-16 | 318942 | 0.00000000 | 204.00 |
| tenant-17 | 337083 | 0.00000000 | 159.00 |
| tenant-18 | 326703 | 0.00000000 | 167.00 |
| tenant-19 | 319044 | 0.00000000 | 201.00 |
| tenant-20 | 337110 | 0.00000000 | 159.00 |
| tenant-21 | 325876 | 0.00000000 | 168.00 |
| tenant-22 | 319167 | 0.00000000 | 202.00 |
| tenant-23 | 336876 | 0.00000000 | 160.00 |

| Coordinator observation | First | Last |
| --- | ---: | ---: |
| managedBytes | 3200000 | 51121952 |
| allocatedBytes | 3191912 | 3875715164856 |
| handles | 0 | 0 |
| gen0 | 0 | 407030 |
| gen1 | 0 | 288927 |
| gen2 | 0 | 178531 |

Use resources.jsonl for chronological RSS, CPU, GC and worker-accounting trends. k6.log contains per-tenant minute windows (completed operations and maximum latency). Whole-run percentiles can conceal transient stalls; sampled RSS growth alone is not proof of a leak.

Latency covers complete validated operations; explicit cancellation includes drain/confirmation. Expected injected provider errors are separate from unexpected errors. A short successful pilot does not qualify multi-hour stability.

## Independently verified completion

Actual k6 duration: 14,400.81021 seconds. All recorded thresholds passed. No forced termination, cleanup errors, remaining owned process groups or worker workspace entries; supervisor and idle-sleep inhibitor exited. Maximum sampled pending-removal age: 0.3454199 seconds. No admission or maintenance failure events occurred.

Gateway RSS stayed around 110–123 MiB in later samples. k6 RSS increased separately from about 260 MiB after warmup to roughly 600 MiB before falling to about 541 MiB at the final sample. This load-generator growth remains relevant for longer tests; it did not violate the unchanged resource limits. Individual latency reached 4,845 ms despite the passing whole-run and per-tenant p99 thresholds. This evidence qualifies this four-hour native test profile, not arbitrary duration, hostile-plugin isolation or Windows capacity.

See four-hour-completion.json for portable metrics, artifact identities and hashes of local raw evidence.
