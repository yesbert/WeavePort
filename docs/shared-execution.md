# Shared plugin execution

Use Shared ownership when many tenants can safely reuse expensive immutable state, such as a loaded model. One resident worker handles several unary calls concurrently. Your application supplies tenant identity for every client view; a plugin cannot choose another tenant's callback authority through its input.

Start with [a verified installation and one approval](installed-plugin-clients.md). The [C# example](../examples/shared/README.md) shows the complete catalog-to-client path. Set `PluginApproval.Ownership = WorkerReusePolicy.Shared`, choose `Degree` per worker and `Workers`, then call `host.ShareAsync`. Use `plugin.For(authenticatedTenant)` for ordinary `CallAsync` calls. Dispose each view when finished and the shared instance when its service lifetime ends. The host also owns cleanup of registered shared instances.

| Ownership | Use it for | State and execution |
|---|---|---|
| `CustomerBound` | Tenant-specific connectors and mutable tenant state | Exclusive worker; state affinity by default |
| `ApprovedSessions` | Reviewed functions whose registered resources can be cleaned between tenants | Sequential reuse after cleanup acknowledgement |
| `Shared` | Reviewed, concurrency-safe computation with expensive shared state | Resident workers, concurrent unary calls, shared process failure boundary |

Shared SDK applications opt into concurrent protocol 2. C# uses `ConcurrentCalls = true`; Python uses `concurrent_calls=True`; TypeScript uses `new PluginApplication('1', { concurrentCalls: true })`. The manifest's launch declares only Shared ownership and compatibility protocol 2. Exclusive launches declare protocol 1. Use separate launch artifacts when the same source supports both startup modes. Older hosts refuse protocol 2 rather than silently running a concurrent worker under serial assumptions.

Shared calls keep independent invocation contexts, callback IDs and grants. Do not retain a context beyond its invocation, store tenant secrets in global caches, or assume one tenant owns the process. Shared streams and binary sources are refused before dispatch; use an exclusive installation for those operations. Shared execution currently supports native `ProcessProfile`, not Docker or MCP adapters.

Cancellation returns promptly and revokes callback authority. Work that continues in the plugin keeps its slot until a terminal response or worker retirement. This prevents falsely advertising free capacity. `CancellationGrace` and `MaximumAbandonedCalls` bound that retention. A callback or handler failure can fail one invocation; malformed protocol, channel loss and silence can retire the whole worker. In-flight calls are never automatically replayed because external effects may already have happened.

`MaximumRestarts` within `RestartWindow` bounds recovery. Exhaustion exposes a disabled instance instead of restarting forever. `ISharedPlugin.Snapshot` reports ready workers, active and abandoned calls, restart count and disabled state. Raw stderr remains off unless `ForwardStandardError` is explicitly enabled; it can contain tenant data.

Choose memory reservations and concurrency from measurements on the deployment machine. Resident workers charge the host budget even when idle. A higher degree is not guaranteed to increase throughput: Python synchronous CPU libraries may release the GIL, async I/O can overlap, and TypeScript CPU work requires author-managed worker threads. Functional concurrency tests and performance benchmarks answer different questions.

Concurrent SDKs release pending callback state before acknowledging a cancelled invocation. They retain at most 4096 exact callback identities to consume known late replies once; unknown, repeated or expired replies are protocol errors. Active invocations retain their own callbacks independently of this bound.
