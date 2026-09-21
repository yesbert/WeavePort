## Purpose

Define verified local coordinator behavior for pristine worker assignment, bounded tenant reservations and release across Docker and trusted local profiles. Enforced resource and security boundaries depend on the selected profile; distributed scheduling and kernel/engine isolation failures are outside this tested surface.

## Requirements

### Requirement: Shared pristine reserve
A coordinator SHALL hold a bounded reserve of pristine customer-unassigned workers keyed by resolved deployment, version, reuse policy and execution reservations. Initial pristine checkout SHALL be exclusive; subsequent approved reuse SHALL use the separate acknowledged-clean lifecycle. Local executable/script stability SHALL remain a trusted-deployment prerequisite.

#### Scenario: Two customers acquire the same plugin
- **WHEN** A and B concurrently acquire default customer-bound workers from the reserve
- **THEN** each receives its own execution environment with no prior customer state

### Requirement: Accounted execution capacity
The host SHALL bound global worker and memory reservations, simultaneous starts and pristine residency. Exclusive allocations SHALL additionally obey tenant budgets; resident shared workers SHALL charge only the global budget at the configured concurrency memory estimate. Cleanup-uncertain reservations SHALL remain unavailable until confirmed removal.

#### Scenario: Capacity exhausted
- **WHEN** an invocation requires a new worker but the configured capacity is reserved
- **THEN** it is rejected without dispatch and capacity is released only after confirmed worker removal

#### Scenario: One tenant exhausts its quota
- **WHEN** A reserves its tenant worker-count or memory allowance while global capacity remains
- **THEN** further A allocations are rejected without dispatch and B can still allocate within its own allowance

### Requirement: Explicit idle policy
Customer-bound bindings SHALL preserve state by default, with explicit opt-in idle or pressure eviction for reconstructible state. Approved clean sessions SHALL follow reusable retention. Shared workers SHALL remain resident outside pristine and idle eviction until disposal or failure.

#### Scenario: Idle release and reuse
- **WHEN** a customer-bound binding exceeds its configured idle duration
- **THEN** its used worker is destroyed and its next call obtains a fresh worker

#### Scenario: Stateful default
- **WHEN** a customer-bound binding has no idle-release policy
- **THEN** maintenance does not discard its process state

### Requirement: Bounded host registration lifetime
Disposed bindings SHALL be removed from host registration, while shared callback admission SHALL remain effective until outstanding callbacks complete.

#### Scenario: Detached callback survives disposal
- **WHEN** a callback ignores cancellation and its binding is disposed
- **THEN** a new binding for the same tenant cannot bypass the existing callback limit

### Requirement: Adapter-aware local lifecycle
Trusted local workers SHALL share binding authority, admission and pool policies, while APIs SHALL distinguish memory reservations from enforced resource ceilings. Customer-bound used local workers SHALL never move to another customer. Approved local sessions SHALL remain cooperative same-user execution.

#### Scenario: Trusted replacement
- **WHEN** a customer-bound cooperative local worker writes state and is replaced for another customer
- **THEN** the replacement receives a fresh process and workspace with its own context

#### Scenario: Local cleanup limit
- **WHEN** local cleanup is unconfirmed
- **THEN** its reservation remains unavailable without claiming kernel-enforced containment of escaped descendants

### Requirement: Exceptional lifecycle completion
Invocation setup failures SHALL release acquired admission. Unsupported invocation deadlines SHALL be rejected before binding registration. Cancellation callback failures SHALL NOT prevent independent worker cleanup and binding deregistration; outstanding callbacks SHALL retain admission until actual completion.

#### Scenario: Setup throws before dispatch
- **WHEN** invocation setup throws after admission
- **THEN** a subsequent invocation and binding disposal can acquire admission

#### Scenario: Cancellation callback throws
- **WHEN** disposal encounters a throwing cancellation callback
- **THEN** worker cleanup and deregistration are attempted and cleanup errors are reported after those attempts

### Requirement: Safe shutdown resource release
Coordinator and binding shutdown SHALL close admission before releasing owned cancellation resources, request cancellation of admitted work and release those resources after operations using them finish. Concurrent and repeated disposal SHALL observe one shutdown outcome. Cleanup failures SHALL remain observable and SHALL NOT skip independent release attempts or erase uncertain worker reservations.

#### Scenario: Worker startup races shutdown
- **WHEN** shutdown begins while an admitted worker is starting
- **THEN** startup is cancelled and accounted for before its owned cancellation resources are released, and no worker is published for subsequent use

#### Scenario: Repeated disposal after a cleanup error
- **WHEN** concurrent disposal callers encounter a throwing cancellation callback or failed worker cleanup
- **THEN** callers observe the same completed shutdown outcome, independent release attempts still run and uncertain worker reservations remain visible

#### Scenario: Another binding remains usable
- **WHEN** tenant A disposes a binding while tenant B uses the same plugin artifact and version
- **THEN** B retains its independent execution and callback authority, and A's detached callbacks retain admission until actual completion

### Requirement: Trusted adapter setup
Native Unix sockets SHALL use exclusively created owner-only temporary directories. Docker execution SHALL use an absolute executable selected by the trusted deployment, without resolving it through the process PATH.

#### Scenario: Private socket allocation
- **WHEN** multiple native Unix endpoints are created
- **THEN** each owns a distinct private directory and releases it when disposed, including before listening

#### Scenario: Docker executable override
- **WHEN** a deployment supplies a relative Docker executable path
- **THEN** setup rejects it before executing any command

### Requirement: Approved reusable worker pool
The host SHALL default to customer-bound execution and SHALL permit operator-approved native sessions to return a worker only after a valid cleanup acknowledgement. Reusable workers SHALL remain globally accounted, have no assigned tenant, retain exact normalized deployment/version compatibility and expire under an explicit reusable-idle policy. Legacy or MCP workers SHALL NOT silently enter the approved pool.

#### Scenario: Successful cleanup
- **WHEN** a reviewed compatible SDK session acknowledges cleanup
- **THEN** another compatible binding can acquire that worker exclusively and reuse counters reflect the assignment

#### Scenario: Cleanup failure or cancellation
- **WHEN** cleanup fails, an acknowledgement is invalid or the invocation deadline expires
- **THEN** the worker is retired and never reassigned as clean

#### Scenario: Old binding disposed
- **WHEN** a binding that has returned its clean worker is disposed or restarted
- **THEN** it does not destroy a worker subsequently assigned to another binding

#### Scenario: Explicit restart
- **WHEN** an approved binding is explicitly restarted
- **THEN** its next acquisition bypasses previously used clean workers without destroying another binding's active worker

#### Scenario: Idle or incompatible demand
- **WHEN** a clean worker expires or capacity is needed for an incompatible deployment
- **THEN** it may be removed while reservation accounting remains until removal is confirmed
