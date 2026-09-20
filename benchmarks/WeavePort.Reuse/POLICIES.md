# Approved-session and bound-container benchmarks

This experiment follows the two agreed policies without adding a sandbox product or changing the production host:

- `approved`: one call per warm interpreter/container, followed by cooperative registered-session cleanup; compatible customer/plugin switches may reuse it. Unregistered global state remains an accepted risk.
- `bound`: retain a container only for the exact customer/plugin/version. The global pool prefers matching resident bindings and replaces the least-recently-used idle container when necessary. A previously used container is never transferred across the binding.

`matrix_policy.py` implements this experimental routing on the existing direct Engine transport. The fixture uses two public compatible Python modules in one immutable image. It does not qualify private customer code visibility, real callback authorization, other languages or hostile native-code containment.

The `session` workload creates a registered customer cache, opens/writes/reads a temporary file, changes the environment, and returns the normal tenant/plugin result. It checks for stale registered state before each invocation. Cleanup clears registered state and restores the environment; hidden globals remain a deliberate negative control.

## Replay and interpretation

Use `arrival: periodic`, `population`, `periodSeconds` and `callsPerCustomer` for repeated low-frequency customers. Set `seconds` to `periodSeconds * callsPerCustomer`. Phases are reproducible uniform random offsets within each period. Each customer retains its plugin unless `switchPlugins` is set; that control changes the plugin in successive periods. `pluginsPerCustomer: 2` selects the fixture's two available module identities; periodic replay normally assigns just one of them to each customer.

Poisson replay assigns one unique customer per invocation for the approved throughput tests. Pool sizes are benchmark comparison points, not permanent product caps. Each slot has a 64 MiB configured ceiling in this series; observed working-set memory is reported separately. Preparation is excluded from offered-traffic time and recorded separately. Request latency includes queueing, replacement and session cleanup. A fully served periodic customer requires every planned call to succeed.

The one-second SLO requires no incorrect or dropped calls and aggregate p99 <= 1 second. High-rate qualification additionally checks time-window p99 and bounded backlog growth using `matrix_capacity.stability`. Repeat points with different seeds and reverse their order. Low-rate periodic points lack enough calls per time window for the high-rate qualification, and must be described separately.

## Reproduction

```sh
docker build -f benchmarks/WeavePort.Reuse/Matrix.Dockerfile \
  -t weaveport-reuse-policy:20260920 benchmarks/WeavePort.Reuse
WEAVEPORT_MATRIX_IMAGE=weaveport-reuse-policy:20260920 \
  python3 tools/performance/matrix_policy_checks.py artifacts/policy-checks-new
WEAVEPORT_MATRIX_IMAGE=weaveport-reuse-policy:20260920 \
  python3 tools/performance/reuse_matrix.py path/to/config.json artifacts/policy-results-new
python3 -m unittest discover -s tools/performance -p 'test_matrix*.py'
```

Use a new output directory for each run. Only exact generated container names may be removed. A Docker preparation timeout is infrastructure failure, not measured saturation; retain it and repeat the point. Partial parallel preparation is settled before cleanup and exact owned names are reconciled after removal.
