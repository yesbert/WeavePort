## Experiment
- [x] Implement runtime and author examples for scoped cleanup.
- [x] Test alternating tenants/plugins, permission expiry, tracked/untracked resources and cleanup failure.
- [x] Compare turnover and identical infrequent-customer arrival schedules on Docker Desktop.
- [x] Retain metrics, limits and negative evidence; do not change production reuse guarantees.

Evidence: `reports/benchmarks/trust-reuse-air-20260919/`. Both fixture revisions pass 42 expected-behavior checks, including deliberate trusted-mode global-state leakage. Two turnover repetitions, identical 500-customer arrival replays and an additional 5,000-customer retained-process probe complete with correct results and confirmed cleanup. Fresh-container replay fails the separate one-second p99 target. Host-execution guard and local artifact pinning added to the final prototype. Production SDK/host behavior remains unchanged.
