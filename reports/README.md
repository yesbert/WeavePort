# Retained evidence

Reports describe the source, packages, environment and workload recorded in each dated result. Directory names such as `current` were chosen when those baselines were retained; they do not qualify the current checkout. See [current product and source status](../docs/status.md) and [historical interpretation](../docs/history.md).

- [0.1.0-internal.2 candidate](release/0.1.0-internal.2/candidate/report.md) and [offline distribution](release/0.1.0-internal.2/distribution/report.md): exact package provenance used by the historical distribution packager.
- [0.1.0-internal.2 benchmark baseline](benchmarks/current/README.md): retained workload results and exact measured identities.
- [Repository qualification on 2026-09-13](verification/current/README.md): the reduced checkout and relocated tooling at the recorded source revision.
- [Runtime optimization and soak preparation](optimization/runtime-and-soak/README.md): source-built comparisons and harness qualification, separate from delivered-release measurements.
- [Public release records](release/): qualification, audits and publication evidence for their named versions.

Raw logs, generated benchmark projects and intermediate runs belong under ignored `artifacts/`. Retained JSON, compressed samples, logs, API diffs and reproduction source snapshots are historical evidence. Preserve their bytes and recorded defects; use maintained harnesses under `tests/`, `benchmarks/` and `tools/` for new work. In particular, `reserve-race-before` deliberately preserves the defective earlier implementation. Historical reproduction scripts are tied to their original checkout and machine paths, not supported current entry points.

Do not delete release manifests while tooling or installed recovery still references them. A new audit records its own verification; it never relabels old measurements as a fresh result.
