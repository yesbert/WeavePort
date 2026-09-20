# Cross-customer reuse experiment

This is a benchmark prototype, not a production SDK/runtime change. It compares the same small Python operation over Docker CLI stdio using two actual plugin modules and alternating synthetic customers:

- `fresh`: create a container, invoke once, confirm removal before the next invocation.
- `process`: retain a supervisor/container, create a fresh Python child per invocation, terminate its process group, sweep writable fixture directories and audit descendants before returning the slot.
- `trusted`: retain container/interpreter, run SDK-style registered cleanup and resource checks before returning the slot.

Both tiny plugin modules are preinstalled in one compatible Python image. This does not test arbitrary dependency installation, incompatible versions or switching languages. Production pool keys would need approved runtime, dependency set, artifact versions and trust policy.

The latter two modes assume cooperation. The trusted hidden-global negative control deliberately demonstrates cross-customer leakage; a successful reset is not a hostile-plugin isolation guarantee. Read the [author examples and boundaries](AUTHOR-GUIDE.md).

```sh
docker build -f benchmarks/WeavePort.Reuse/Dockerfile \
  -t weaveport-reuse-experiment:1 benchmarks/WeavePort.Reuse
python3 tools/performance/reuse.py --output artifacts/reuse/isolation-new --phase isolation
python3 tools/performance/reuse.py --output artifacts/reuse/turnover-new --phase turnover --repeats 2
python3 tools/performance/reuse.py --output artifacts/reuse/population-new --phase population --repeats 1
```

Every output directory must be new. Containers use the same pinned Python base, no network or host mounts, read-only root, unprivileged UID, all capabilities dropped, no-new-privileges, a 64 MiB memory/swap ceiling and a 64-process ceiling. Only the trusted measuring host controls Docker. No existing services are stopped.

## Meaning of the measurements

Turnover uses one lane, at least 200 unique customers and at least ten seconds per variant; up to 50,000 calls bounds the experiment. Each customer receives one call and every lane alternates plugin A/B. This is a saturation capacity control, not a production arrival distribution. Each repeat reverses variant order. Retained-container preparation is timed separately. Throughput includes removing each fresh container; response latency and slot-turnover latency are reported separately so retirement cost cannot disappear from the comparison.

Population replay uses 500 different customers, one invocation each over 60 seconds, a reproducible random phase and four lanes. Each lane allows only one complete invocation/cleanup at a time. Every mode receives the same schedule for a given seed. Offered load is 8.33 calls/second; latency begins at the intended arrival and includes queue time. This is a single minute of low-frequency customer traffic, not a steady-state or multi-hour qualification. The four lanes are a controlled experiment, not a production pool limit. Correctness, cleanup and the separate one-second p99 target are recorded independently.

One-shot Docker Engine memory samples run independently of requests; macOS pressure and swap growth trigger stop. Working-set memory and raw cgroup accounting are distinct from host/CLI RSS. Sampling may miss transient peaks, especially short-lived child processes and fresh containers. The host's Python load generator and measurement code are included in host RSS. No measured number can be transferred directly to the released .NET host, other languages, large payloads, real dependencies or hostile plugins.

The runner retains per-call identities, timings, outcomes, resource samples and source/image hashes. Synthetic customer identifiers/payloads contain no real customer data. Every explicit removal targets an exact generated name. A failure is not a valid capacity result. The resource observer uses the [Docker Engine stats API](https://docs.docker.com/reference/api/engine/version/v1.51/) with `stream=false&one-shot=true`.

An additional `--phase population --modes trusted --clients 5000 --repeats 1` control uses the same four lanes and one call per customer per minute. It proves only that offered synthetic load, not a maximum customer count.

For the broader security, workload, direct-Engine transport and sustained-arrival experiment, see [the matrix protocol](MATRIX.md).

The [extended MacBook Air evidence](../../reports/benchmarks/reuse-matrix-air-20260919/README.md) records pool scaling, repeated arrival-rate boundaries, grouped customers, fault recovery, long-run memory, production-host controls and the corrected kernel-IPC boundary. Experimental reuse remains separate from the production host contract.

The [two-policy protocol](POLICIES.md) tests the agreed simpler choice: approved cooperative sessions across customers, or containers bound to one customer/plugin/version with affinity and eviction. It includes repeated low-frequency customer traffic and explicit hidden-state negative controls.
