# Runtime diagnostics

Pass an application-owned `ILogger<PluginHost>` to `new PluginHost(logger, options: limits)`. The existing constructor uses a null logger; the library does not configure global logging or own the logger's lifetime. Hosting references Microsoft.Extensions.Logging.Abstractions 10.0.11 (MIT), whose net10.0 dependency is Microsoft.Extensions.DependencyInjection.Abstractions 10.0.11 (MIT). The internal offline feed includes both original packages with license metadata.

| Event ID | Level | Meaning | Structured fields |
|---|---|---|---|
| 1001 | Warning | Worker startup failed | WorkerInstance, ErrorType |
| 1002 | Warning | Application callback failed | WorkerInstance, CorrelationId, ErrorType |
| 1003 | Warning | Invocation failed | WorkerInstance, CorrelationId, Stage, ErrorType |
| 1004 | Error | Cleanup failed | WorkerInstance, ErrorType |
| 1005 | Error | Background maintenance failed | ErrorType |
| 1006 | Warning | Host admission rejected before dispatch | Reason |

IDs belong to WeavePort.Hosting. Correlation IDs are generated for invocations. `prepare` includes deadline, diagnostic setup and worker startup; `exchange` begins when dispatch may have happened. Capacity refusals and cooperative cancellation are not ordinary plugin failure events. Cancellation callback failures during disposal are cleanup events.

No exception object or message, stack trace, payload, configuration, credential, operation name or raw worker stderr is passed to these events. Worker stderr remains drained to avoid blocking the process. Plugin-side generic SDK errors remain generic; these events do not promise plugin stack traces. Treat any custom log provider and its access/retention policy as application infrastructure.

Existing activities expose transport measurements; `PluginHost.Snapshot` exposes reservations, registrations, retained callback admission, quarantine and the last maintenance failure type. A cleanup event does not establish that an external effect failed. Follow the [native recovery runbook](native-operations.md) for uncertain application outcomes.

Admission reasons are fixed codes (`concurrent-starts`, `pool-reservations`, `tenant-reservations`, `session-call`, `tenant-calls`). These identify host gates; the SDK client has its own serialization gate. Limits and busy rejection semantics are unchanged.
