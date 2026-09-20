# Approved-session and bound-container measurements — MacBook Air M4

2026-09-20. Experimental Python/Docker path; not production .NET host capacity. 10 CPUs, 32 GiB host RAM, approximately 24 GiB Docker allocation; unrelated services remained running. Each container is limited to 1 CPU and 64 MiB. Fixed pool sizes are experimental comparison points, not product caps.

See [protocol](../../../benchmarks/WeavePort.Reuse/POLICIES.md). Both policies use the shared-interpreter fixture: approved sessions permit compatible customer/plugin changes after registered cleanup; bound sessions require an exact customer/plugin/version match and destroy an evicted container before reassignment. These are two small preinstalled public modules, not private production artifacts.

Every session writes a registered customer cache and temporary file and mutates the environment. Cleanup closes/removes/resets these resources. Unregistered hidden globals deliberately remain in approved mode; that accepted risk is positively demonstrated by a negative control. No production authorization or hostile-native-code claim is made.

Policy controls: 82/82 expected checks passed. Matrix unit tests and preparation-regression evidence are retained under verification. Failed Docker preparation is retained separately and excluded from throughput interpretation.

## Measurements

Poisson: one unique customer per call. Periodic: one call/customer/30 seconds, two periods; fully served means both calls succeeded. Calls/s includes successful late calls; qualification additionally requires zero loss and p99 <= 1 second. High-rate Poisson qualification checks time-window p99 and backlog growth. Preparation is measured separately. RAM is sampled container working set, not configured limits or total host memory.

| Series | Policy | Arrival | Seed | Pool | Population or offered calls/s | Correct calls/s | Fully served customers | p99 ms | Peak MiB | Dropped | SLO |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| main | approved | poisson | 1729 | 16 | 2000 | 1999.01 | 59973 | 293.43 | 182.70 | 0 | True |
| main | approved | poisson | 1729 | 16 | 4000 | 4003.78 | 120131 | 108.48 | 185.57 | 0 | True |
| resume | approved | poisson | 1729 | 16 | 6000 | 6005.19 | 180168 | 199.05 | 190.68 | 0 | True |
| resume | approved | poisson | 1730 | 16 | 6000 | 5985.34 | 179566 | 987.90 | 191.06 | 10 | False |
| resume | approved | poisson | 1730 | 16 | 4000 | 3890.42 | 116718 | 1007.75 | 186.78 | 2807 | False |
| resume | approved | poisson | 1730 | 16 | 2000 | 1988.99 | 59670 | 39.32 | 181.34 | 0 | True |
| resume | bound | periodic | 1729 | 64 | 64 | 2.13 | 64 | 49.43 | 692.84 | 0 | True |
| resume | bound | periodic | 1729 | 128 | 128 | 4.27 | 128 | 40.91 | 1381.77 | 0 | True |
| resume | bound | periodic | 1729 | 64 | 256 | 8.52 | 256 | 2104.44 | 682.27 | 0 | False |
| resume | bound | periodic | 1729 | 64 | 1024 | 19.25 | 413 | 4664.94 | 479.85 | 830 | False |
| resume | bound | periodic | 1730 | 64 | 1024 | 16.34 | 303 | 5923.24 | 482.47 | 1007 | False |
| resume | bound | periodic | 1730 | 64 | 256 | 8.49 | 256 | 644.01 | 681.73 | 0 | True |
| resume | bound | periodic | 1730 | 128 | 128 | 4.27 | 128 | 37.78 | 1382.41 | 0 | True |
| resume | bound | periodic | 1730 | 64 | 64 | 2.13 | 64 | 43.00 | 692.62 | 0 | True |
| resume | bound | periodic | 1729 | 64 | 64 | 2.13 | 64 | 424.03 | 692.39 | 0 | True |
| refinement | approved | poisson | 1731 | 16 | 1000 | 1000.85 | 30027 | 16.88 | 179.86 | 0 | True |
| confirmation | approved | periodic | 1734 | 16 | 30000 | 999.99 | 30000 | 5.65 | 180.73 | 0 | True |
| confirmation | approved | poisson | 1734 | 16 | 1000 | 997.01 | 119642 | 5.94 | 184.81 | 0 | True |

The bound plugin-switch control is marked by switchPlugins in its config. Never equate correct calls/s with distinct fully served customers/s in partially successful runs. A single passed point is not a repeated capacity bound. Compare all repeats, including failures; these finite shared-laptop runs do not establish a universal server maximum.

## Evidence

planned-configurations/ preserves requested sequences, including stages not run after the memory guard; only completed stage summaries establish results. results.json contains derived summaries; each series retains image/source identities and per-stage summaries/configs. samples.jsonl.gz contains chronological resource observations. checks/ contains real Docker boundary and fault controls. The initial main-series preparation timeout happened before any offered call; the exact remaining container was reconciled and later preparation waits for all concurrent starts before cleanup. Subsequent source identities record this harness correction. Refinement at 2000 offered calls/s was interrupted by the host memory guard after system swap grew by over 1 GiB; owned container working set was approximately 183 MiB. This incomplete run is not a valid capacity point, and no cause of the system-wide memory load is established.
