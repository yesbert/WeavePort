# Runtime diagnostics

Observe worker startup, invocation, callback and cleanup failures through your application’s existing logging. Diagnostic events expose lifecycle context while excluding plugin payloads and secrets.

Pass an application-owned `ILogger<PluginHost>` to `new PluginHost(logger, options: limits)`. The existing constructor uses a null logger; the library does not configure global logging or own the logger's lifetime. Hosting references Microsoft.Extensions.Logging.Abstractions 10.0.12 (MIT), whose net10.0 dependency is Microsoft.Extensions.DependencyInjection.Abstractions 10.0.12 (MIT). The internal offline feed includes both original packages with license metadata.

| Event ID | Level | Meaning | Structured fields |
|---|---|---|---|
| 1001 | Warning | Worker startup failed | WorkerInstance, ErrorType |
| 1002 | Warning | Application callback failed | WorkerInstance, CorrelationId, ErrorType |
| 1003 | Warning | Invocation failed | WorkerInstance, CorrelationId, Stage, ErrorType |
| 1004 | Error | Cleanup failed | WorkerInstance, ErrorType |
| 1005 | Error | Background maintenance failed | ErrorType |
| 1006 | Warning | Host admission rejected before dispatch | Reason |
| 1007 | Information | Explicitly enabled raw stderr | WorkerInstance, DiagnosticLine |

IDs belong to WeavePort.Hosting. Correlation IDs are generated for invocations. `prepare` includes deadline, diagnostic setup and worker startup; `exchange` begins when dispatch may have happened. Capacity refusals and cooperative cancellation are not ordinary plugin failure events. Cancellation callback failures during disposal are cleanup events.

Events 1001–1006 do not include exception objects/messages, stack traces, payloads, configuration, credentials, operation names or raw stderr. Worker stderr remains drained to avoid blocking the process. Plugin-side generic SDK errors remain generic; these events do not promise plugin stack traces. Treat any custom log provider and its access/retention policy as application infrastructure.

Existing activities expose transport measurements; `PluginHost.Snapshot` exposes reservations, registrations, retained callback admission, quarantine and the last maintenance failure type. A cleanup event does not establish that an external effect failed. Follow the [native recovery runbook](native-operations.md) for uncertain application outcomes.

Admission reasons are fixed codes (`concurrent-starts`, `pool-reservations`, `tenant-reservations`, `session-call`, `tenant-calls`). These identify host gates; the local SDK client follows host unary admission. Inspect `host.Scheduling` for queue and active-call accounting and `ISharedPlugin.Snapshot` for ready workers, active/abandoned calls, restart count and disabled state.

`ForwardStandardError = true` on the operator approval or native profile enables event 1007. It can expose tenant data. Lines are truncated at 4096 characters; one queue per host retains at most 128 lines and drops older queued lines under pressure. Delivery never blocks pipe draining. Logger failure disables optional delivery. A blocked caller logger may leave one background task alive after host disposal; disposal does not wait for arbitrary logger code.
