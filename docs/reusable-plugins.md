# Writing plugins for approved session reuse

Introduced in core packages 0.4.0 and Python/TypeScript author SDKs 0.2.0. Cross-customer reuse is an explicit operator decision with accepted cooperative-state risk. Docker remains the execution boundary; no additional sandbox runtime is required.

## The two policies

| Policy | Worker ownership | Completion |
|---|---|---|
| `CustomerBound` (default) | One immutable binding: customer, plugin, version, configuration and authority | A healthy worker remains with that binding. Replacement, expiry or disposal destroys it. |
| `ApprovedSessions` | A compatible deployment can serve different approved bindings sequentially | SDK cleanup must succeed before the worker becomes available to another binding. |

Approval covers the **whole executable/image and its dependencies**, not just a handler name. Plugins cannot approve themselves. A Docker profile provides container restrictions; a `ProcessProfile` remains explicitly trusted same-user execution even when customer-bound.

Compatibility uses the resolved Docker image or frozen local launch profile, artifact version, transport, resource configuration and reuse policy. Invocation and binding-idle deadlines do not change executable compatibility. The host does not install arbitrary plugins into a used container or share workers between languages. Different logical plugin identifiers may share a worker only when they are served by the same compatible approved deployment. Do not put private code or secrets belonging to other customers into a shared image.

## Operator setup

```csharp
await using var host = new PluginHost(new SchedulingOptions
{
    MemoryBudgetMiB = 4096,
    NormalTimeout = TimeSpan.FromSeconds(5),
    ReusableIdleTimeout = TimeSpan.FromSeconds(30)
});

// Select this only from reviewed, operator-owned deployment configuration.
var profile = new DockerProfile("your-reviewed-image:your-version", MemoryMiB: 128)
{
    ReusePolicy = WorkerReusePolicy.ApprovedSessions
};

// context, callbacks and grants come from the authenticated application.
await using var plugin = await host.BindAsync(context, profile, callbacks, grants);
var result = await plugin.InvokeAsync("$sdk.call",
    JsonSerializer.SerializeToElement(new { operation = "transform", input = new { text = "hello" } }),
    cancellationToken);
```

The scheduler uses one shared budget, never one scheduler per customer. `MaximumWorkers` remains optional; admission uses configured memory reservations and demand. Reservations are not live RSS, and Docker/host overhead needs separate headroom. Idle clean workers can be evicted for incompatible demand and expire after `ReusableIdleTimeout`; they do not count as pristine workers or retain a tenant assignment. Direct hosts expose the same timeout through `WorkerPoolOptions`.

Normal deadlines include SDK cleanup, startup, transport and callbacks. A cleanup error, missing/invalid acknowledgement, cancellation, timeout or worker failure prevents reuse and retires the worker. Do not retry operations with external side effects without an application-level idempotency rule.

## Resource ownership in each SDK

| Language | Register cleanup | Transfer resource ownership |
|---|---|---|
| C# | `context.OnClose(Func<ValueTask>)` | `context.Own(IDisposable)` / `context.OwnAsync(IAsyncDisposable)` |
| Python | `context.on_close(action)`; action may return an awaitable | `context.own(resource)` chooses `aclose()` or `close()` |
| TypeScript | `context.onClose(action)`; action may return a Promise | `context.own(resource)` for resources with `close()` |

The SDK attempts all registered actions in **reverse registration order**. Context callbacks and new registrations are revoked before cleanup begins. The context releases its references to customer identity, configuration and callback dispatch. Retained old contexts reject use; copies made by plugin code remain the author's responsibility. Cleanup failure produces an error, never a successful reusable result.

Ownership transfer means the SDK disposes the object. Do not also dispose it through another owner. Register immediately after creation, before another operation can throw. Prefer lexical `using`/`with`/`try-finally` when the resource has a shorter lifetime; use the context for resources whose lifetime spans the complete invocation or result stream.

The [runnable examples](../examples/reuse/README.md) demonstrate all three languages.

## Customer data and shared state

- Keep payloads, context, authorization, configuration, transaction state and customer caches in local variables or explicitly session-owned objects.
- Keep only immutable, customer-independent metadata at module/static scope. Review mutable default arguments, singleton services, memoization/LRU caches, logger enrichers, event handlers, thread-local/AsyncLocal/ContextVar state and native-library caches.
- Prefer no customer cache across calls. If durable caching is necessary, use an external store with host-authorized tenant scoping; a cache key alone is not authorization.
- Return independently owned JSON data. Cleanup must not clear or mutate the same object that will be serialized as the result.
- Clear sensitive mutable buffers before returning them to an application buffer pool. This reduces retained data; it does not erase immutable strings, copies or runtime memory.
- Do not alter process-wide environment variables, working directory, locale, signal handlers or default credentials per customer. These SDKs do **not** automatically snapshot and restore arbitrary process state.

A deliberately unregistered global can still reveal customer A's value to customer B in approved mode. This is an acknowledged limit, not a prevented attack. SDK registration discipline and review are prerequisites for approval.

## Files and connections

Use a unique directory or file for each session. Register deletion of the exact owned path before opening child resources, then register their close/dispose actions: reverse cleanup closes handles before deleting the directory. Never sweep a shared host directory or follow a customer-supplied cleanup path. Cleanup does not automatically discover unregistered temporary files.

Close readers, cursors, response bodies and streams. Roll back unfinished database transactions. Do not retain a customer-authenticated connection, session cookie, default authorization header or transaction for the next customer. Shared connection pools are acceptable only when their library contract and configuration reliably remove customer state; review that dependency explicitly. Access through host callbacks remains independently authorized by the current binding.

## Parallel work and cancellation

One container executes one invocation at a time. Related parallel subtasks belong inside that invocation, with bounded concurrency. Await every task before returning. Use external orchestration for independent plugin fan-out/fan-in.

Do not start fire-and-forget threads, timers, subprocesses or tasks that can continue into the next customer's session. If work needs a stop signal, register an action that **requests stop and awaits actual completion**. Merely cancelling a token or clearing a timer reference does not prove completion. Unregistered background work is not automatically detected by these SDKs.

For C#, a session-owned cancellation source plus an `OnClose` action can cancel and await the task; register source disposal first so task completion happens before source disposal. Python cleanup can cancel a task and await it while handling only its expected cancellation. TypeScript cleanup can abort a controller and await the associated Promise. A task that ignores stop keeps cleanup incomplete; the host deadline then retires the worker.

Cleanup cannot call host capabilities after context expiry. Finish required external operations before returning from the handler. Keep cleanup short and limited to releasing owned resources.

## Result streams

A stream owns its context/resources from `$sdk.start` until natural completion or `$sdk.close`. Intermediate batches cannot release its worker to another customer. Early consumer exit through the SDK client closes the stream; cancellation/failure can instead retire the worker. Cleanup failure at stream completion makes the operation fail even if earlier batches were delivered.

Use `LocalPluginClient` or the installed client returned by `PluginHost.BindAsync` for result streams. Both queued and immediate host admission support operation-wide residency across stream exchanges and consumer pauses. Shared ownership remains unary-only.

## Review and tests before approval

Exercise sequential A → B → A calls with different customer identities, secrets and grants in the **same worker instance**. Repeat with the same plugin and compatible different logical plugin IDs. Verify current context/callback identity and absence of registered cache/file state. Retain a previous context and confirm it rejects calls.

Include handler exceptions, cleanup exceptions, cleanup that hangs, cancellation, worker crashes, early stream exit and concurrent customers. Verify that failure produces a different execution instance for subsequent work and that no partially cleaned worker is returned. Test a deliberate hidden-global negative control so the accepted limitation remains visible.

Run tests against the actual packaged language SDK, host and deployment image. Treat historical fixture throughput as a hypothesis until the real plugin and dependencies are measured. Approved workers share a failure domain over time; approval is a reviewed operating policy, not an arbitrary-code isolation certificate.

## Operations

`PluginHost.Snapshot` expose `ReusableWorkers`, `ReuseHits`, `SessionReturns`, `SessionCleanupFailures` and `WorkersStarted`, alongside reservations and quarantine. `SessionCleanupFailures` counts explicit SDK cleanup errors and invalid cleanup acknowledgement shapes, not every timeout or arbitrary hidden-state leak. Scheduler queue/latency and failure counters remain available.

Watch low reuse hit rates, many worker starts, cleanup errors and queue tails. A falling hit rate can mean incompatible deployments or short idle retention; it is not automatically a lack of RAM. Hard Docker memory exhaustion retires a failed worker. There is no automatic leak detector or forced garbage collection that makes arbitrary libraries safe for reuse.

## Protocol compatibility

Updated SDKs advertise `sessionCleanup: 1` in the existing native ready envelope and include boolean `reusable` in result envelopes. `true` acknowledges completed registered cleanup; `false` retains session ownership, for example during a stream. The operator's approved policy requires this capability. Legacy workers continue to work with the default customer-bound policy; approved startup rejects them before customer dispatch. MCP workers cannot opt into this native extension.

## Restart and deployment changes

`RestartAsync` stops a worker still owned by the binding and makes its next acquisition bypass previously used clean workers. A worker already returned to the shared pool may now belong to somebody else; restarting or disposing the old binding never destroys another customer's active worker. To revoke approval or discard all shared state after discovering an unsafe dependency, stop affected registrations and dispose/drain their owning host before deploying the corrected profile. A new image digest or local launch profile uses a separate compatibility key.

## Simultaneous shared ownership

`ApprovedSessions` remains sequential cleanup-based reuse. `Shared` instead keeps resident workers and allows concurrent tenant invocations under an approved degree. It requires protocol 2 and concurrency-safe plugin code. It does not clean global state between calls or offer shared streams. Choose explicitly using [one installation approval](installed-plugin-clients.md); review [shared failure and cancellation behavior](shared-execution.md).
