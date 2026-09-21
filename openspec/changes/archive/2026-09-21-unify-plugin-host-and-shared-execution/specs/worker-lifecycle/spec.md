## MODIFIED Requirements

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
