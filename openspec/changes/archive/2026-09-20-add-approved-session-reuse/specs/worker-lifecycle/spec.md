## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Adapter-aware local lifecycle
Trusted local workers SHALL share binding authority, admission and pool policies, while APIs SHALL distinguish memory reservations from enforced resource ceilings. Customer-bound used local workers SHALL never move to another customer. Approved local sessions SHALL remain cooperative same-user execution.

#### Scenario: Trusted replacement
- **WHEN** a customer-bound cooperative local worker writes state and is replaced for another customer
- **THEN** the replacement receives a fresh process and workspace with its own context

#### Scenario: Local cleanup limit
- **WHEN** local cleanup is unconfirmed
- **THEN** its reservation remains unavailable without claiming kernel-enforced containment of escaped descendants

### Requirement: Shared pristine reserve
A coordinator SHALL hold a bounded reserve of pristine customer-unassigned workers keyed by resolved deployment, version, reuse policy and execution reservations. Initial pristine checkout SHALL be exclusive; subsequent approved reuse SHALL use the separate acknowledged-clean lifecycle. Local executable/script stability SHALL remain a trusted-deployment prerequisite.

#### Scenario: Two customers acquire the same plugin
- **WHEN** A and B concurrently acquire default customer-bound workers from the reserve
- **THEN** each receives its own execution environment with no prior customer state

### Requirement: Explicit idle policy
Customer-bound bindings SHALL retain process state by default and permit opt-in idle release. Approved clean sessions SHALL instead follow shared reusable-idle retention and SHALL NOT promise binding-affine process state.

#### Scenario: Idle release and reuse
- **WHEN** a customer-bound binding exceeds its configured idle duration
- **THEN** its used worker is destroyed and its next call obtains a fresh worker

#### Scenario: Stateful default
- **WHEN** a customer-bound binding has no idle-release policy
- **THEN** maintenance does not discard its process state
