# The concepts behind a plugin call

A plugin call connects your application to one approved piece of extension code. The concepts below explain what the host controls and what remains in your application. Your .NET application hosts the runtime and decides when a plugin is called. Plugins implement application-defined functions and asynchronous result streams in C#, Python or TypeScript.

## A binding is an authority boundary

A binding combines the host-selected tenant context, plugin artifact/version, execution profile and callback grants. Request payloads do not choose tenant authority or executable paths. Customer-bound used workers retain tenant affinity. Explicitly approved sequential reuse and Shared ownership have different state and concurrency contracts; neither provides tenant heap isolation.

Exclusive client bindings serialize work under host admission policy; streams retain worker residency for their complete enumeration. Approved Shared installations support concurrent unary tenant calls in resident workers. See [shared execution](../docs/shared-execution.md) for the trust and failure boundaries.

## Callbacks connect plugins to application services

A plugin can ask the host for a capability such as reading a document or looking up knowledge. The host grants the capability explicitly. The callback implementation must still authorize the particular object being accessed; a grant is not permission to access every customer’s data.

## State belongs to the application

Worker memory is temporary. Persist facts, workflow checkpoints and external-action identities in application-owned stores. An interrupted call may have produced an external effect. Recovery and retry require an explicit application policy; cancellation is not proof that nothing happened.

## Share the execution budget

The copyable coordinator template owns one host and shared operation admission. It is application source, not another public package API. Multiple coordinators have independent budgets. Native memory reservations are estimates for admission, not enforced OS memory ceilings.

## Choose execution according to trust

Native execution is for owner-controlled code running with the host OS user's rights. It does not confine hostile plugins. Container execution has a different deployment policy and trust boundary; a profile switch does not prove equivalent qualification.

Read the [architecture](../docs/architecture.md), [integration contract](../docs/v1-integration-contract.md), [worker lifecycle](../docs/worker-lifecycle.md) and [security architecture](../docs/security-architecture.md) for exact boundaries and limitations. [OpenSpec requirements](../openspec/specs/plugin-execution/spec.md) define verified behavior.
