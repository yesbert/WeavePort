## Purpose

Define verified optional scheduling for reconstructible customer plugins with fair shared memory admission, separate launch concurrency and bounded invocation lifetimes. Release qualification is recorded separately from historical capacity measurements.

## Requirements

### Requirement: Unique scheduled registration
The optional scheduler SHALL admit at most one immutable registration per tenant/plugin key, SHALL bound registrations and queued payloads, and SHALL serialize concurrent complete single-exchange calls on a registration onto one exclusive worker. It SHALL reject SDK multi-exchange stream operations before dispatch until operation-wide residency is supported. Direct-host behavior SHALL remain unchanged.

#### Scenario: Concurrent same-plugin work
- **WHEN** a customer submits multiple calls on one registration
- **THEN** admitted calls execute in arrival order without creating concurrent duplicate workers for that registration

#### Scenario: Registration replacement
- **WHEN** a second version or authority is registered for an existing tenant/plugin key
- **THEN** registration is rejected until the previous registration is disposed

#### Scenario: SDK stream requires retained iterator state
- **WHEN** a caller attempts an SDK stream operation on a scheduled registration
- **THEN** it fails before dispatch instead of allowing eviction between stream batches

### Requirement: Fair shared admission
The scheduler SHALL allocate shared count and memory capacity without fixed normal-worker reservations per tenant. It SHALL prefer eligible queued tenants with fewer active workers and rotate equally active tenants by last service. It SHALL NOT preempt active work solely because demand changes.

#### Scenario: Idle capacity borrowing
- **WHEN** one customer submits independent plugins while no other customer needs capacity
- **THEN** it can use all available normal capacity within the global budgets

#### Scenario: Competing customer arrives
- **WHEN** another customer queues work while the first customer occupies capacity
- **THEN** the next eligible free slot serves the less-active customer ahead of more work for the saturated customer

### Requirement: Reconstructible residency
The scheduler SHALL release reconstructible customer-bound workers on idle expiry or pressure by destroying their used environment. Approved clean unary sessions SHALL release binding residency into the compatible reusable pool. A separate pristine reserve SHALL use only recently requested registered profiles and remain bounded independently of customer count.

#### Scenario: Cold customer replaces idle customer
- **WHEN** capacity is full of customer-bound workers and a customer without a resident worker is selected
- **THEN** an idle used worker is destroyed before assigning fresh capacity

#### Scenario: Profile demand disappears
- **WHEN** a profile has no remaining registration or its recent demand expires
- **THEN** its pristine target is removed during maintenance

### Requirement: Bounded heavy execution
The trusted host SHALL authorize normal or heavy class at registration. The scheduler SHALL bound heavy declarations per tenant, heavy concurrent work globally and per tenant, and active heavy memory reservations to at most half of the shared budget. Normal and heavy invocation deadlines SHALL be bounded separately from queue waiting.

#### Scenario: Heavy work cannot consume every slot
- **WHEN** heavy calls reach their shared concurrency limit
- **THEN** further heavy calls wait while eligible normal work can use remaining capacity

#### Scenario: Normal execution expires
- **WHEN** a normal invocation exceeds its deadline including startup and callbacks
- **THEN** the underlying worker is stopped and the result preserves whether execution may have begun

### Requirement: Bounded queue and shutdown
Queue overflow, queue expiry and cancellation SHALL complete without dispatch. Shutdown SHALL close admission, settle pending calls, cancel active work and await cleanup. Cleanup failures SHALL remain observable and close new scheduler admission. Scheduled invocations from callback scopes SHALL be denied to avoid scheduler dependency deadlocks.

#### Scenario: Queue expiry
- **WHEN** a call waits longer than its admission deadline
- **THEN** it completes as busy with no dispatch and queue timing remains observable

#### Scenario: Shutdown with pending work
- **WHEN** the scheduler is disposed with active and queued calls
- **THEN** queued calls become disabled, active calls are cancelled and owned bindings/workers are cleaned up or cleanup failure is reported

### Requirement: Memory-led worker population
The scheduler SHALL default to no independent worker-count ceiling and SHALL admit workers against the shared sum of declared profile memory reservations. Launch concurrency SHALL be bounded separately from resident worker count.

#### Scenario: Growth beyond a benchmark count
- **WHEN** forty independent registered plugins request work under a budget for forty workers and one simultaneous launch
- **THEN** all forty workers can become resident without concurrent-start rejection
- **AND** a forty-first plugin requires idle eviction or waits for memory capacity

#### Scenario: Shared startup occupies the last reservation
- **WHEN** a scheduled customer is admitted while an unassigned worker is still starting in needed capacity
- **THEN** acquisition waits within the invocation deadline instead of returning busy merely because the shared startup is pending
- **AND** it adopts only a compatible pristine profile, gives foreground acquisition precedence over replenishment, and preserves the shared reservation bound
- **AND** cancellation of the wait does not destroy a shared startup that the caller never acquired
- **AND** completion of that shared startup is observable without waiting for unrelated foreground startups

### Requirement: Shared clean-session scheduling
Approved unary SDK sessions SHALL integrate with global fair admission and release binding residency when returning a clean worker. Multiple registered customers SHALL be served without requiring one resident worker per customer. Bound sessions SHALL retain their existing affinity and eviction rules.

#### Scenario: Many registrations and a small pool
- **WHEN** many approved customer-plugin registrations invoke sequentially or concurrently within a small shared budget
- **THEN** clean workers are reused within that budget, identities remain correct and abandoned binding residency does not block admission

### Requirement: Completion releases active scheduling ownership
A scheduled call SHALL release its active scheduling state and signal its idle lifecycle before its public completion task becomes observable as completed. Post-completion lifecycle operations SHALL NOT be rejected solely because the completed call still appears active.

#### Scenario: Immediate lifecycle operation
- **WHEN** a caller awaits a successful scheduled invocation and no other call has been admitted on that registration
- **THEN** the completed call is no longer active and an immediate restart can proceed
