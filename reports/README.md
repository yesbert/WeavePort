# Current evidence

- [Release 0.1.0-internal.2 candidate](release/0.1.0-internal.2/candidate/report.md): exact package/fixture provenance used by the distribution packager.
- [Release 0.1.0-internal.2 distribution](release/0.1.0-internal.2/distribution/report.md): offline installation and consumer validation.
- [Current benchmarks](benchmarks/current/README.md): compact results and exact measured identities.

Raw logs, benchmark-generated projects, repeated fixture identities and intermediate runs belong under ignored `artifacts/`. Retain one compact current baseline here; older reports remain in Git. Do not delete active release manifests while tooling or installed recovery references them. See the [history index](../docs/history.md).

[Current repository qualification](verification/current/README.md) verifies the reduced checkout and relocated tooling without changing the current release identity.

[Runtime optimization and soak preparation](optimization/runtime-and-soak/README.md) records source-built candidate comparisons and harness qualification separately from the delivered release baseline.
