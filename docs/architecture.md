# Architecture and decisions

WeavePort adds plugin execution to the application you already own. It is a library embedded in each application, with domain contracts, authorization and durable state supplied by that application. One `PluginHost` owns one explicit shared worker budget; creating multiple hosts creates independent budgets. It is not a distributed scheduler or a machine-wide singleton.

## Ownership

- Applications authenticate callers, select immutable tenant/plugin/profile identity and grant callback operations. Plugin payloads cannot establish authority.
- Applications own business schemas, workflow state, journals, idempotency and reconciliation. Domain concepts stay out of the platform.
- One Hosting coordinator owns exclusive sessions, resident shared workers, per-tenant admission, deadlines, pristine workers, explicit idle eviction and cleanup accounting. Customer-bound workers are never reassigned. Operator-approved native SDK sessions may share compatible cleaned workers under the [cooperative reuse contract](reusable-plugins.md).
- Author SDKs own protocol details; the client SDK owns bounded unary/stream/source calls and stream residency leases. A gateway selects a preconfigured binding through a credential and cannot register executables remotely.

## Package boundaries

`WeavePort.Abstractions` defines the shared contracts. Hosting depends on Abstractions, Sdk.Client for installed-client binding, caller-owned logging abstractions and the [runtime declaration parsers](optional-dependencies.md#core-hosting-runtime-dependencies). The author SDK has no hosting dependency. `WeavePort.Sdk.Client` adapts host sessions. Optional Composition and Gateway packages implement separate concerns; applications choose them explicitly. Testing helpers belong to verification and are not application prerequisites.

All current core packages target .NET 10. The [compatibility matrix](package-compatibility.md) identifies exact versions; API/protocol version 1 does not establish equality of artifact bytes.

See [source organization](source-organization.md) for package dependency direction, feature folders and multilingual SDK ownership.

## Execution boundary

Native `ProcessProfile` launches explicitly trusted same-user code with private cooperative workspaces and stdio or opt-in Unix sockets. Admission memory reservations are not hard resource limits. Native processes cannot contain malicious code or escaped descendants.

`DockerProfile` requests container resource and operating-system restrictions. Effective protection depends on the actual engine/kernel/deployment. Its regression fixtures remain available; historical container measurements do not qualify the current native release for a new platform.

## Lifecycle decisions

Keep stateful workers by default; idle release and prewarming are opt-in. Bound both tenant and shared capacity. Cleanup-uncertain workers remain reserved and unavailable. Disposed sessions release registration, while outstanding callbacks retain their own admission until actual completion.

Cancellation means that the caller stopped waiting, not that an external action failed. Complete independent cleanup attempts even when cancellation callbacks throw, report failures and retain uncertain business outcomes. See [worker lifecycle](worker-lifecycle.md) and the [recovery runbook](native-operations.md).

## Deferred work

The 0.7.0 package family includes the four core packages and independently selectable Composition and Gateway server/client packages. Windows qualification, stronger native containment, distributed scheduling, application signing/notarization and automatic updates/migrations remain separate work. Integrations with the owner's applications follow their own development schedule.

The [historical evidence guide](history.md) explains where earlier design alternatives, measurements and rejected experiments are retained, including the limits of public access to pre-baseline history. This guide retains the decisions that still govern current code.

## Optional MCP protocol

Current source also supports explicitly selected local MCP tools through the same process lifecycle; native remains the default. See [MCP plugins](mcp-plugins.md) for exact revisions, result semantics and support limits. MCP does not introduce AI concepts or implicit host authority into native plugins.

Shared ownership is an explicit trusted-code choice: several tenant invocations share process memory, while each invocation retains its own immutable callback authority. A worker crash can fail all of its in-flight calls. Cancellation revokes callbacks promptly but retains capacity until completion or retirement. See [shared execution and recovery](shared-execution.md).
