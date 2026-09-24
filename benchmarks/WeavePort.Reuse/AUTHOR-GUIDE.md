# Author examples for cooperative reuse

These examples use the **experimental** `fixture/scope.py` API. They are not part of the published WeavePort SDK. At the time of this experiment, the separate `ScheduledPluginHost` retained customer-bound workers. The current unified host also supports explicitly [approved SDK reuse](../../docs/reusable-plugins.md) and [Shared ownership](../../docs/shared-execution.md); use those guides for production API integration. Examples reduce mistakes; they do not make unchecked code safe.

## Put customer data in the invocation

The two executable fixtures are [plugin A](fixture/plugin_a.py) and [plugin B](fixture/plugin_b.py). Their normal handlers receive a scope and return customer-specific results without retaining that scope globally.

```python
def execute(scope, payload):
    # This scope's authority expires at the end of this invocation.
    scope.call_host("read")
    normalized = payload.upper()
    return {"tenant": scope.tenant, "value": normalized}
```

Avoid module globals, class attributes, closure caches or retained context objects containing customer data. A cache key containing a tenant ID is not sufficient if arbitrary plugin code can enumerate the whole cache. Only immutable customer-independent code/configuration belongs in a shared process cache. The negative control `leave_hidden_canary` deliberately violates this rule: the next plugin can read the previous tenant's data even after cooperative cleanup reports success.

## Register resources before returning

```python
def execute(scope, payload):
    with (scope.workspace / "input.txt").open("w") as output:
        output.write(payload)

    scope.background(lambda stop: stop.wait(30))
    # Real background work must check stop promptly and complete its own finally blocks.
    # Use scope.child(...) for child processes, not detached or independent sessions.
    return {"tenant": scope.tenant, "value": "written"}
```

Use invocation-owned temporary storage. Close streams, database connections, sockets and sessions with context managers or registered cleanup. Register cleanup as soon as acquisition succeeds. Do not launch fire-and-forget tasks, keep threads alive, start detached children, change signal handlers, alter shared modules, or leave writable data outside the invocation workspace. Avoid putting customer secrets in logs, environment variables, exception messages or plugin output unrelated to that request.

If an SDK manages an in-memory customer cache, register its clearing function immediately:

```python
cache = {}
scope.on_close(cache.clear)
```

Clearing a Python collection is not a guarantee that secret bytes are erased from process memory. A plugin requiring stronger confidentiality must use a stronger execution boundary.

## Finish internal parallel work before returning

Do not request additional plugin workers for parallel pieces of the same invocation. Bound internal concurrency and join the work before returning:

```python
from concurrent.futures import ThreadPoolExecutor

def normalize(value):
    return value.strip().upper()

def execute(scope, payload):
    # A small illustrative limit, not an entitlement to extra host CPU or memory.
    with ThreadPoolExecutor(max_workers=2) as tasks:
        values = list(tasks.map(normalize, payload.splitlines()))
    return {"tenant": scope.tenant, "value": "\n".join(values)}
```

Each parallel operation must observe its own bounded I/O deadlines and the invocation's cancellation when the real SDK exposes it. Executor shutdown joins tasks; it cannot make an indefinitely blocked operation safe. Internal work still shares the invocation's CPU, memory and execution deadline. Do not retain the executor globally or let customer work continue after the result. A long-running operation must be declared and authorized as heavy work by the host policy; an author-provided label alone grants no extra budget. Fan-out across different registered plugins belongs to the host scheduler, which controls fairness and the shared pool.

## Cleanup can fail

The prototype revokes scope callbacks before cleanup. Registered background tasks must stop; registered child process groups are killed and reaped. It restores the environment and working directory, sweeps the fixture's writable `/tmp` and `/dev/shm`, and checks for extra descriptors, threads and processes. The process variant additionally runs each invocation in a fresh child interpreter. Cleanup failure, residual processes, crash or invocation timeout causes the host to remove the exact owned container before replacing it.

The fixture's `call_host` is a local permission/expiry test stub, not the production host callback implementation. Real host authorization must be enforced outside plugin-controlled memory. A plugin's self-reported reset success is not proof of isolation. Audits are finite and can miss arbitrary in-process state, runtime hooks, dynamically loaded modules and malicious attempts to bypass the supervisor. Same-UID process replacement within a container is also not a general hostile-code security boundary.

## Trust policy belongs to the application

Choose shared-process reuse only for explicitly reviewed, cooperative artifacts and customers whose isolation requirements permit it. Approval must bind artifact/version, allowed dependencies and capabilities. A new version requires a new decision. Unchecked community plugins retain separate execution environments across customers. The experiment does not establish protection against kernel/engine vulnerabilities or side channels.

Treat the shared image's readable files as visible to every plugin using that image. Do not bake customer secrets or tenant-private plugin artifacts into a shared bundle. The two public fixtures in this experiment do not qualify confidentiality between private customer code bundles.

A customer-bound container can remain warm for later calls from that same customer/plugin binding. Stronger separation does not require destroying it after every normal call. It must not be reassigned to a different customer after use. The matrix's `fresh` case deliberately measures the more expensive per-call replacement extreme.

Kernel-managed state also needs an owner and explicit cleanup. System V shared memory, semaphores and message queues, and POSIX message queues can outlive your Python process. Close/detach alone is not necessarily deletion. Register the matching removal action as soon as acquisition succeeds, and use an invocation-owned namespace/name. The extended fixture deliberately leaves kernel shared memory behind and proves that a later customer could read it before the coordinator audit was added. The experimental coordinator now refuses container reuse if kernel IPC resources remain; it does not trust an in-plugin cleanup-success flag. This is another reason ordinary process exit is not a complete customer-isolation policy.
