# Runtime diagnostics

**Source availability:** This checkout includes qualified changes made after the published `v0.7.0` release. Package labels still read .NET `0.7.0` and Python/TypeScript `0.3.0`; those labels do not make locally rebuilt artifacts identical to the published downloads. Structured `PluginFailure` details, `DetailedFailureSink`, event 1008 and combined primary/cleanup error metadata describe current source, not the original published packages. Use the matching release tag for a published-package integration; see [current status](status.md).

Observe worker startup, invocation, callback and cleanup failures through your application’s existing logging. Diagnostic events expose lifecycle context while excluding plugin payloads and secrets.

Pass an application-owned `ILogger<PluginHost>` to `new PluginHost(logger, options: limits)`. The existing constructor uses a null logger; the library does not configure global logging or own the logger's lifetime. Hosting references Microsoft.Extensions.Logging.Abstractions 10.0.12 (MIT), whose net10.0 dependency is Microsoft.Extensions.DependencyInjection.Abstractions 10.0.12 (MIT). The internal offline feed includes both original packages with license metadata.

| Event ID | Level | Meaning | Structured fields |
|---|---|---|---|
| 1001 | Warning | Worker startup failed | WorkerInstance, ErrorType |
| 1002 | Warning | Application callback failed | WorkerInstance, CorrelationId, ErrorType |
| 1003 | Warning | Invocation failed | WorkerInstance, CorrelationId, Stage, ErrorType, Code, CleanupFailed |
| 1004 | Error | Cleanup failed | WorkerInstance, ErrorType |
| 1005 | Error | Background maintenance failed | ErrorType |
| 1006 | Warning | Host admission rejected before dispatch | Reason |
| 1007 | Information | Explicitly enabled raw stderr | WorkerInstance, DiagnosticLine |
| 1008 | Warning | Detailed diagnostic destination failed | ErrorType |

IDs belong to WeavePort.Hosting. Correlation IDs are generated for invocations. `prepare` includes deadline, diagnostic setup and worker startup; `exchange` begins when dispatch may have happened. Capacity gates use event 1006. Invocation failures handled by the worker pool emit event 1003, including caller cancellation (`cancelled`) and host shutdown (`disabled`). Filter by `Code` rather than treating every warning as an author defect; cancellation/disabled outcomes do not mark the invocation activity as an error. Cancellation callback failures during disposal are cleanup events.

Events 1001–1006 and 1008 do not include exception objects/messages, stack traces, payloads, configuration, credentials, operation names or raw stderr. Worker stderr remains drained to avoid blocking the process. Plugin-side generic SDK errors remain generic; these events do not promise plugin stack traces. Treat any custom log provider and its access/retention policy as application infrastructure.

Artifact version mismatch is an actionable startup failure, separate from malformed protocol frames. Authorized callers receive `version-mismatch` and a structured `VersionMismatch` containing `Expected` and `Advertised`; shared startup/prewarming exposes `PluginVersionMismatchException.Mismatch`. No domain invocation was dispatched. Version strings are protocol metadata and are not added to events 1001–1006. Inspect these details in application-owned diagnostics rather than logging arbitrary plugin frames.

Existing activities expose transport measurements; `PluginHost.Snapshot` exposes reservations, registrations, retained callback admission, quarantine and the last maintenance failure type. A cleanup event does not establish that an external effect failed. Follow the [native recovery runbook](native-operations.md) for uncertain application outcomes.

Admission reasons are fixed codes (`concurrent-starts`, `foreground-priority`, `pool-reservations`, `tenant-reservations`, `session-call`, `tenant-calls`). These identify host gates; the local SDK client follows host unary admission. Inspect `host.Scheduling` for queue and active-call accounting and `ISharedPlugin.Snapshot` for ready workers, active/abandoned calls, restart count and disabled state.

`ForwardStandardError = true` on the operator approval or native profile enables event 1007. It can expose tenant data. Lines are truncated at 4096 characters; one queue per host retains at most 128 lines and drops older queued lines under pressure. Delivery never blocks pipe draining. Logger failure disables optional delivery. A blocked caller logger may leave one background task alive after host disposal; disposal does not wait for arbitrary logger code.

## Stable exception codes

`PluginCallException.ErrorCode` equals its existing `Status`. `MayHaveExecuted` remains the authority for whether dispatch may have occurred; a code alone does not authorize a retry. `PluginVersionMismatchException.ErrorCode` is `version-mismatch`, with versions available separately through `Mismatch`. Both exceptions retain their `IOException` category.

| Code | Meaning and response |
| --- | --- |
| `busy` | Admission or binding concurrency refused work. Apply caller-owned backpressure. |
| `disabled` | Host/plugin lifetime ended or shared recovery is disabled. Inspect lifetime and worker health. |
| `cancelled`, `timeout` | Waiting ended; reconcile possible external effects before retrying. |
| `denied`, `binding-denied` | Callback authority or gateway binding refused access. Check trusted grants/credentials. |
| `failed`, `sdk-error` | Execution failed; inspect safe host diagnostics and application-owned outcome state. |
| `protocol-error` | Invalid worker exchange. Inspect protocol compatibility; do not reuse uncertain state. |
| `version-mismatch` | Artifact identity disagrees before dispatch. Correct the installation or binding. |
| `input-limit` | Request exceeds the configured input bound. Reduce or chunk input. |
| `value-limit`, `stream-limit`, `chunk-limit` | Output or source bounds were exceeded. Partial delivery is not success. |
| `invalid-mode`, `source-unsupported` | The selected operation or session cannot provide the requested mode. Check capabilities. |
| `cleanup-error` | SDK resource cleanup failed. Retire the worker; do not return it to the clean pool. |

Directly constructing `PluginCallException` preserves the supplied `Status`/`ErrorCode`. Native and gateway wire boundaries normalize unrecognized peer codes to `unknown-error`; callers must still have a fallback. Standard argument, cancellation and transport exceptions are not converted into a universal platform exception. Python `SessionCleanupError.code` and TypeScript `SessionCleanupError.code` expose `cleanup-error` while retaining their cause/aggregate information; the C# SDK's internal cleanup exception carries the equivalent code. Existing wire values remain stable; current source adds optional failure metadata.

## Event ownership and response

The source-generated methods in Hosting's `Diagnostics/RuntimeLog.cs` reference the named constants in `RuntimeLogEvents.cs`. Numeric IDs and existing event names remain stable and must not be reused for another meaning.

| Event name | ID | Operational response |
| --- | --- | --- |
| `StartupFailed` | 1001 | Check artifact, runtime and launch configuration. |
| `CallbackFailed` | 1002 | Inspect application callback health and grants using the correlation ID. |
| `InvocationFailed` | 1003 | Distinguish preparation from exchange; reconcile uncertain effects. |
| `CleanupFailed` | 1004 | Inspect quarantine and retained reservations; do not assume resources were released. |
| `MaintenanceFailed` | 1005 | Inspect background maintenance and retry/cleanup state. |
| `AdmissionRejected` | 1006 | Identify the fixed gate reason and apply backpressure or adjust an explicit budget. |
| `StandardError` | 1007 | Opt-in raw diagnostics only; apply application retention and access controls. |
| `DiagnosticDeliveryFailed` | 1008 | Inspect the protected destination; its failure does not replace the invocation outcome. |

## Logging ownership and exception messages

Exceptions are **not globally logged**. Constructing or throwing an exception does not emit a log. Without an injected logger, the hosting events use a null logger. The library does not subscribe to global first-chance or unhandled-exception hooks.

| Failure boundary | Owner and visibility |
| --- | --- |
| Host worker startup, invocation, callback, cleanup and maintenance | Hosting emits the applicable generated event when that boundary handles the failure. The same failure can cause distinct callback/invocation or cleanup events; these describe different lifecycle stages. |
| Argument/configuration validation, installation discovery and client-side validation | The exception or refusal reaches the caller. These paths do not promise a Hosting event. The consuming application's request/job boundary decides whether and how to record it. |
| Python, TypeScript and C# author handlers | SDKs report bounded wire error codes and perform owned cleanup. They do not promise a global logger or transfer arbitrary exception text/stacks. |
| Cooperative cancellation and ordinary capacity refusal | Capacity gates may emit event 1006; worker-pool cancellation handling may emit event 1003 with its dedicated code. Application policy should distinguish expected cancellation from defects. |

Keep actionable fixed messages on standard exceptions. They describe the failing invariant and can change independently of the code contract. Handle platform failures using `ErrorCode`/`Status`, structured metadata and the standard exception type. Event IDs identify log events; error codes identify caller-visible outcomes. Neither implies automatic retry safety. Application logging should avoid duplicate reports of the same boundary and must apply its own policy before including exception messages, stacks or inner exceptions.

## Structured failure details

`InvocationResult.Failure` and `PluginCallException.Failure` carry a safe `PluginFailure` with `Code`, `Phase`, `CorrelationId` and `CleanupFailed`. The existing status and `ErrorCode` remain available for compatibility; an SDK failure can retain status `failed` while `Failure.Code` identifies `sdk-error` or `cleanup-error`. `MayHaveExecuted` remains authoritative for execution uncertainty, never automatic retry permission. Old peers may omit details.

The existing invocation event 1003 now includes the safe code and cleanup outcome; there is no duplicate classification event.

Configure `WorkerPoolOptions.DetailedFailureSink` only for a protected, bounded destination. It is called synchronously and must return promptly; enqueue into an application-owned bounded sink instead of blocking the host. It receives local exception details, including aggregate worker startup/invocation and teardown causes, and may expose sensitive messages or stack traces. The application owns access controls, retention and delivery; default logging remains safe. Exceptions in the destination do not replace the original outcome. Remote SDK exception text is never transported, so host diagnostics cannot reconstruct a plugin stack trace.

Exclusive invocation classification uses `cancelled` for caller cancellation, `disabled` for host lifetime termination and `timeout` for an expired owned deadline. An unrelated cancellation or unexpected internal state in that boundary is `internal-error`; explicitly malformed protocol remains `protocol-error`. Shared author/callback failures and channel retirement have their own codes and can affect different scopes; metadata may be absent on early refusal or older peers.

Gateway clients use transport status categories instead of parsing gRPC detail text: unavailable, resource-exhausted, invalid-argument, not-found, unimplemented, timeout, binding-denied, denied, gateway-failed or unknown-error. Recognized platform codes travel separately. Unknown peer values use `unknown-error`. See the [failure code catalogue](failure-codes.md) for meanings. Full shared vocabulary is maintained in [the protocol contract](../contracts/protocol.json).
