- [x] Implement and verify pristine fork-server child isolation and trust boundaries.
- [x] Extend fault/residue combinations and preserve negative controls.
- [x] Run scaling/workload/arrival matrix and optimize measured bottlenecks.
- [x] Repeat selected limits, run soak and verify cleanup/memory stability.
- [x] Measure dormant registry scaling; optimize only with before/after and scheduler regressions.
- [x] Retain report, reproducible commands, identities and remaining limits.

Verification: both 30-minute operating points complete with every invocation correct, within one second, and no drops or container replacements during traffic. The trusted/fork-server rows respectively serve 8,098,973 and 990,096 distinct customer identities per run. Expected security controls pass 195/195 for both Engine and CLI; source and packed scheduler consumers pass 88 assertions each, packed Hosting passes, and 34 harness controls pass. Style, maintained links, public-tree and strict OpenSpec checks pass. Final evidence is retained in `reports/benchmarks/reuse-matrix-air-20260919/`.

Actual-host controls retain the reserve-race baseline and corrected runs. Two-period Python population staircases compare 4/8-GiB reservation budgets, followed by counterbalanced 256-customer checks with additional seeds. Both budgets pass three of four 256-customer runs; occasional latency violations prevent qualifying that population across all repetitions. The conservative confirmed lower population is 128 at one call/customer/minute, distinct from the much faster reuse prototype. These are finite source-candidate measurements, not qualification of committed HEAD, a release, or production cross-customer reuse integration.
