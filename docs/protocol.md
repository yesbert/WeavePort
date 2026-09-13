# Invocation protocol

The executable reference implementations are `plugins/csharp`, `plugins/python` and `plugins/typescript`. These intentionally include fault operations and must be run inside the resource-limited test containers.

## Wire contract

UTF-8, one JSON object per line. The worker emits `{"type":"ready","protocol":1,"pluginVersion":"1"}` first. The host rejects a protocol or version mismatch. Each host message is:

```json
{"type":"invoke","id":"host-generated-id","operation":"search","payload":{"query":"weave"},"context":{"tenant":"A","plugin":"demo","version":"1","profile":"default","configuration":{}},"traceId":"host-trace"}
```

The worker returns `{"type":"result","id":"host-generated-id","value":...}` or `{"type":"error","id":"host-generated-id","code":"plugin-error"}`. Errors do not carry plugin diagnostics or secrets to consumers.

To call the host, emit `{"type":"callback","id":"host-generated-id","callbackId":"unique-within-call","operation":"documents.read","payload":{}}`. The host responds with `{"type":"callback-result","id":"host-generated-id","callbackId":"unique-within-call","value":...}`. The worker waits for that response before continuing. Invocation mismatches, duplicate callback IDs, malformed or oversized frames terminate that session. The default stdio transport reserves stdout for protocol and continuously drains stderr without accumulation. The opt-in [Linux transport (historical) — pre-public record](history.md) uses the same frames on its dedicated socket; detached containers use Docker's disabled log driver.

Frames are limited to 1 MiB of UTF-8 and parsed to depth 32. Worker envelopes reject duplicate reserved fields (`type`, `id`, `callbackId`, `operation`, `payload`, `value`, `protocol`, `pluginVersion`, `code`), including escaped spellings. Ready protocol versions must be the supported integer; invalid numeric conversion becomes a protocol error. Nested payload/result JSON retains application semantics. One call runs at a time per binding; additional calls return busy. One root invocation allows eight callbacks including nested invocations. The host deadline includes startup, transport and callback waits. Root callback grants and tenant identity constrain nested execution; traces are retained. Same-session reentry fails busy rather than deadlocking.

## Authority and outcomes

Only trusted application code creates bindings and selects images, Docker contexts and callback grants. Images resolve to immutable IDs at bind time. Worker version handshake prevents a mismatch between the advertised binding and the actual worker. Two profiles or versions get separate execution state even with the same tenant.

The host constructs `HostCall.Context` from its immutable binding and never accepts authority from plugin JSON. Each callback implementation must validate object ownership and request arguments. The demonstration rejects a forged tenant and mediates external service access through a local HTTP test service. No network or Docker control socket is exposed to plugins. The local socket transport exposes only the worker's own read-only endpoint directory; the default stdio transport needs no host mount.

`InvocationResult.Status` is `ok`, `busy`, `disabled`, `cancelled`, `timeout`, `failed`, `denied`, or `protocol-error`. `MayHaveExecuted` becomes true when dispatch begins. A timeout, cancellation, process failure or denied later callback may follow an already committed external effect; none implies safe retry. Host-owned idempotency keys and reconciliation determine what to do. Compensation is independently fallible.

A terminated invocation's Value records exitCode, oomKilled and runningAtTermination when an instance existed. Successful Value is the operation-specific contract. Instance is a diagnostic identity, never an authorization token. Cross-tenant nested denial exposes neither that identity nor foreign termination diagnostics. Backend provisioning or cleanup failures may throw rather than returning an invocation status; Docker-engine availability is shared infrastructure.

## Lifecycle and resource profile

The default Docker profile uses 256 MiB memory with no extra swap allowance, 0.5 CPU, 64 PIDs, a read-only root, a private 16 MiB tmpfs, uid/gid 65532, no capabilities, no-new-privileges and no network. Binding creation is lazy; no container starts until invocation. Restart removes that binding's process and writable state; the application may replay persisted state. Dispose disables the binding and cancels an active call.

Admission is coordinator-local and fail-fast, with a separate bounded callback lease retained until the callback actually finishes. Stopping the wait cannot abort arbitrary in-process callback code. Trusted callbacks must cooperate; a detached callback cannot repeatedly free its capacity while still running. The library does not provide distributed quotas, durable invocation recovery or host-kernel isolation.

## Sample domain surface

`search` accepts a query and returns an array of `{id,text}`. `reduce` accepts application-owned state, amount and simulation time and returns state plus events. `workspace` and `subprocess` exercise native plugin facilities in the sandbox. These are sample operations, not hardcoded domain concepts in the hosting packages.

An application can expose a domain HTTP endpoint by calling its selected binding. WeavePort does not register arbitrary plugin HTTP routes or assume that a request's tenant field is authenticated. Public store installation, schema evolution negotiation and production packaging/signing are later product work.

See the [hostile-plugin threat model](security-architecture.md) and [security evidence (historical) — pre-public record](history.md) before interpreting these controls as production security guarantees.
