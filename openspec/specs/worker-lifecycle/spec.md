## Purpose

Define verified local coordinator behavior for pristine worker assignment, bounded tenant reservations and release across Docker and trusted local profiles. Enforced resource and security boundaries depend on the selected profile; distributed scheduling and kernel/engine isolation failures are outside this tested surface.

## Requirements

### Requirement: Shared pristine reserve
A coordinator SHALL hold a bounded reserve of customer-unassigned workers keyed by resolved artifact or trusted local launch profile, version and execution reservations and SHALL assign each worker exclusively at most once. Local executable/script stability SHALL be an explicit trusted-deployment prerequisite rather than an immutable-image claim.

#### Scenario: Two customers acquire the same plugin
- **WHEN** A and B concurrently acquire workers from the shared reserve
- **THEN** they receive distinct execution environments with their own bound context and no prior customer's state

### Requirement: Accounted execution capacity
The coordinator SHALL bound reserved worker count and configured memory reservations both globally and per tenant, simultaneous launches and pristine residency; cleanup-uncertain workers SHALL remain reserved and unavailable for assignment. Whether a memory reservation is also an enforced worker ceiling SHALL be identified by the execution profile.

#### Scenario: Capacity exhausted
- **WHEN** an invocation requires a new worker but the configured capacity is reserved
- **THEN** it is rejected without dispatch and capacity is released only after confirmed worker removal

#### Scenario: One tenant exhausts its quota
- **WHEN** A reserves its tenant worker-count or memory allowance while global capacity remains
- **THEN** further A allocations are rejected without dispatch and B can still allocate within its own allowance

### Requirement: Explicit idle policy
A binding SHALL retain its process state by default and SHALL permit opt-in release of idle execution environments without revoking the binding.

#### Scenario: Idle release and reuse
- **WHEN** an opted-in binding exceeds its idle duration while no invocation is active
- **THEN** its used environment is destroyed and its next invocation obtains a fresh environment with the original immutable binding

#### Scenario: Stateful default
- **WHEN** a binding has no idle-release policy
- **THEN** maintenance does not discard its process state

### Requirement: Bounded host registration lifetime
Disposed bindings SHALL be removed from host registration, while shared callback admission SHALL remain effective until outstanding callbacks complete.

#### Scenario: Detached callback survives disposal
- **WHEN** a callback ignores cancellation and its binding is disposed
- **THEN** a new binding for the same tenant cannot bypass the existing callback limit

### Requirement: Adapter-aware local lifecycle
Trusted local workers SHALL share binding authority, admission, pristine assignment and idle/restart/disposal policies, while reports and APIs SHALL distinguish scheduling memory reservations from enforced resource ceilings. Used local workers SHALL never be assigned to another customer.

#### Scenario: Trusted replacement
- **WHEN** a cooperative local worker writes instance state and is released before another customer binds the same fixture
- **THEN** the replacement receives a fresh process and workspace with its own bound context

#### Scenario: Local cleanup limit
- **WHEN** a local worker is stopped or cleanup fails
- **THEN** root-process termination is bounded and uncertain cleanup retains its reservation, without claiming kernel-enforced containment of escaped descendants

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
