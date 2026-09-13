# Security and trust boundaries

The first product version runs only owner-controlled plugins. Its qualified native process runtime is **not a hostile-code sandbox**. Separate processes provide lifecycle ownership and fault handling; they do not isolate code from the host user's files, network or credentials. See [architecture](architecture.md), [execution profiles](local-execution.md) and [current qualification](status.md).

## Current responsibilities

| Boundary | Platform responsibility | Application responsibility |
| --- | --- | --- |
| Installation | Validate immutable artifacts and exact compatibility metadata | Decide which owner-controlled artifacts may execute |
| Tenant binding | Preserve host-selected context and operation grants | Authenticate users and select their authorized installation |
| Callback | Route bounded calls with immutable binding context and cancellation | Check object ownership, argument shape, destinations and side effects |
| Protocol | Enforce envelope identity, UTF-8, byte/depth limits and reserved-field rules | Treat plugin results as domain input requiring validation |
| Lifecycle | Own worker leases and retain uncertain cleanup state | Preserve recovery evidence and reconcile external outcomes |
| Resource admission | Apply the selected execution profile and configured admission bounds | Measure deployment headroom and choose application limits |

Treat data intentionally sent to a plugin as disclosed to it. Configuration is not a credential vault. Prefer narrow host callbacks that select tenant-scoped credentials inside the application. A timeout does not establish that a side effect failed; retry only under explicit idempotency rules. See [native operations](native-operations.md) and [worker lifecycle](worker-lifecycle.md).

## Optional Docker adapter

Docker regression fixtures remain under `tests/` and `plugins/`. Their restricted container profile and adversarial parser/callback scenarios cover a different execution surface. Ordinary containers share a kernel and do not establish arbitrary hostile-plugin production support. Docker measurements from the PoC are historical, not the current native package baseline.

Public third-party plugins, stronger sandboxes, a privileged lifecycle broker and cross-node security protocols require separate design and qualification. The earlier threat assessment and rejected alternatives remain retrievable through the [history index](history.md); they are not current release requirements or implemented guarantees.
