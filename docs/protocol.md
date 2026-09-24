# Invocation protocol

This reference describes the current host/worker wire contract. Authors normally use the [language SDKs](plugin-sdk.md), which own framing, callbacks and resource lifetimes. [Shared definitions](../contracts/protocol.json) generate internal constants for all three SDK languages; independent wire fixtures verify the values.

The maintained author examples are in [examples/sdk](../examples/sdk). The older `plugins/csharp`, `plugins/python` and `plugins/typescript` adapters deliberately include resource-exhaustion faults and require their restricted test containers. They are not native author templates.

## Wire contract

Messages are UTF-8 JSON objects, one per line, with a 1 MiB frame ceiling and maximum depth 32. Stdio reserves stdout for the protocol; the optional native Unix socket and Docker socket transports carry the same frames. MCP uses its [separate explicitly selected protocol](mcp-plugins.md).

An exclusive worker starts with a ready envelope:

```json
{"type":"ready","protocol":1,"pluginVersion":"1","sessionCleanup":1}
```

`sessionCleanup` is an additive capability required for approved sequential reuse. Legacy customer-bound workers can omit it. The host checks protocol and artifact version independently. The invocation envelope contains immutable host-selected authority:

```json
{"type":"invoke","id":"host-generated-id","operation":"search","payload":{"query":"weave"},"context":{"tenant":"A","plugin":"demo","version":"1","profile":"default","configuration":{}},"traceId":"host-trace"}
```

The worker replies with `{"type":"result","id":"host-generated-id","value":{}}` or `{"type":"error","id":"host-generated-id","code":"sdk-error"}`. `value` is application-owned JSON. Errors do not transport raw plugin exception messages or stacks.

A callback uses `{"type":"callback","id":"host-generated-id","callbackId":"unique-within-call","operation":"documents.read","payload":{}}`. The host returns a matching `callback-result` with `value`, or a denied/error outcome. Each callback reply is correlated to its invocation. Unknown invocation IDs, duplicate callback IDs and malformed or oversized frames fail the exchange; shared malformed traffic can retire the whole channel.

Native worker envelope validation rejects duplicate reserved fields, including escaped spellings: `type`, `id`, `callbackId`, `operation`, `payload`, `value`, `protocol`, `pluginVersion`, `code`, `sessionCleanup`, `reusable`, `concurrentCalls`, `degree`, `primaryCode`, `cleanupFailed`. Required fields and their types are validated at the owning boundary. Nested application payloads retain their domain semantics. Ready protocol revisions must be supported integers.

## Authority and outcomes

Trusted application code selects an approved native executable or Docker image, immutable context and callback grants. Docker tags resolve to immutable image IDs at binding; native installations require stable verified deployment files. `HostCall.Context` comes from the binding, never plugin-supplied authority. Callback implementations still authorize each object, argument and effect. Nested plugin calls from callback scopes are denied before dispatch.

Exclusive bindings serialize work under configured queued or fail-fast admission. The default callback budget is eight per internal invocation and is configurable. Invocation deadlines include startup, transport and callback waits. A stopped wait cannot forcibly stop arbitrary in-process callback code; outstanding callbacks retain admission until they actually finish.

`InvocationResult.Status` separates successful execution (`ok`) from admission, cancellation, lifetime, worker, version and protocol failures. See the complete [failure catalogue](failure-codes.md). `MayHaveExecuted` becomes true when dispatch may have begun. Timeout, cancellation, worker loss or a denied later callback can follow a committed external effect. No code authorizes automatic retry; the application owns idempotency and reconciliation.

Successful `Value` is the operation-specific result. Failure results can contain adapter termination observations such as `exitCode`, `oomKilled` and `runningAtTermination`; these are diagnostic observations, not authority or proof that an external effect failed. `Instance` is a diagnostic identity. Cross-tenant nested denial exposes no foreign instance or termination data. Some provisioning, validation and disposal failures throw instead of returning a result.

## Structured errors in current source

**Availability:** These optional failure metadata fields are included in release 0.8.0; older peers may omit them. See [migration notes](releases.md#080-failure-contracts-and-code-quality).

Known wire codes are normalized through the shared catalogue; unrecognized peer codes become `unknown-error`. A combined author-handler and registered-resource cleanup failure keeps the legacy `cleanup-error` frame while adding the primary category and cleanup flag:

```json
{"type":"error","id":"host-generated-id","code":"cleanup-error","primaryCode":"sdk-error","cleanupFailed":true}
```

The host exposes `PluginFailure(Code, Phase, CorrelationId, CleanupFailed)` separately from the legacy status and execution-uncertainty flag. Old peers may omit additive fields. Phases are `prepare`, `exchange` and gateway `transport`. Host worker startup/invocation failures also retain their primary category when teardown fails. Raw local causes are available only through the explicitly configured [diagnostic sink](runtime-diagnostics.md); remote plugin stacks cannot be reconstructed from codes.

## Approved session cleanup extension

Cleanup-capable native SDK workers advertise `sessionCleanup: 1` and a boolean `reusable` on successful results. `ApprovedSessions` requires this startup capability. `true` acknowledges completed registered cleanup; `false` retains the session, for example while a stream remains open. Missing or invalid required acknowledgements fail the exchange. Cleanup errors and invocation deadlines retire the worker. Customer-bound profiles keep affinity regardless of readiness for sharing. MCP cannot opt into this native extension.

Registered resources are cleaned in reverse order. Forced process termination can prevent author cleanup from running. This cooperative mechanism does not erase hidden globals or arbitrary runtime memory; see [the ownership contract](reusable-plugins.md).

## Concurrent protocol 2

Shared SDK workers advertise `protocol: 2` and `concurrentCalls: 1`; protocol-1-only hosts refuse them. The host sends `configure` with the approved `degree` before dispatch (maximum 1024). One reader routes results and callbacks by invocation ID, and serialized writes prevent frame interleaving. Each invocation retains its context and callback authority. Native Shared supports concurrent unary calls, not streams, binary sources, Docker or MCP.

Shared cancellation sends a per-invocation `cancel`, revokes callback authority and retains the occupied slot until terminal acknowledgement or bounded retirement. Process/channel failure affects its in-flight calls without replay. Restart budgets, silence deadlines and cancellation grace belong to operator approval. Exclusive native protocol 1 instead uses worker retirement for active cancellation; it has no separate soft-cancel exchange.

## SDK operations and limits

The SDK maps public function calls to `$sdk.call`; JSON result streams use `$sdk.start`, `$sdk.next` and `$sdk.close`. Binary sources use `$sdk.source.open`, `$sdk.source.read` and `$sdk.source.close`. Application operation names travel inside the SDK request. These reserved operations belong to the SDK, not the application's domain model.

Unary input/output is limited to 512 KiB, stream items to 128 KiB, batches to 16 items and 256 KiB, and total JSON stream data to 64 MiB. Empty unfinished batches are valid heartbeats; the producer retains one pending iterator advancement. Binary sources transfer 4–256 KiB decoded blocks as bounded base64 JSON, defaulting to 64 KiB. An exclusive client keeps one operation lease across reads, consumer pauses and cleanup. Independent result-scope quotas bound large source collection.

## Execution restrictions

Native profiles are trusted same-user processes with cooperative private workspaces and admission memory reservations; they do not enforce filesystem/network isolation or hard resource ceilings. Docker's default profile requests 256 MiB memory, no extra swap, 0.5 CPU, 64 PIDs, a read-only root, private 16 MiB tmpfs, uid/gid 65532, no capabilities, no-new-privileges and no network. Effective Docker protection depends on the deployment. The default stdio adapter requires no host mount; the socket fixture mounts only its endpoint directory, never a Docker control socket.

stderr is drained independently; raw event 1007 forwarding is opt-in and may disclose plugin data. Bindings start lazily, while explicit prewarming/shared startup can start workers earlier. Restart/disposal and uncertain cleanup follow [worker lifecycle](worker-lifecycle.md). Public packages and installed-artifact sealing are implemented; publisher signing, public plugin-store installation, distributed quotas and hostile-plugin containment remain separate work. See [security boundaries](security-architecture.md).
