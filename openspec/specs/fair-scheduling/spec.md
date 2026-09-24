## Purpose

Define unified host scheduling for exclusive and explicitly approved concurrent plugins with fair shared admission, retained operation residency, separate launch concurrency and bounded invocation lifetimes. Release qualification is recorded separately from historical capacity measurements.

## Requirements

### Requirement: Unique scheduled registration
The unified host SHALL offer bounded queued or zero-wait admission under one worker budget. It SHALL serialize exclusive registrations and admit shared invocations up to their approved degree. Streams SHALL retain worker residency across all exchanges and consumer pauses. Registration limits and queued payload bounds SHALL remain effective.

#### Scenario: Concurrent same-plugin work
- **WHEN** a customer submits multiple calls on one exclusive registration
- **THEN** admitted calls execute in arrival order without creating concurrent duplicate workers for that registration

#### Scenario: Registration replacement
- **WHEN** a second version or authority is registered for an existing tenant/plugin key
- **THEN** registration is rejected until the previous registration is disposed

#### Scenario: SDK stream requires retained iterator state
- **WHEN** a caller attempts an SDK stream operation on a scheduled registration
- **THEN** it retains its worker until stream completion or cleanup, including between batches

### Requirement: Fair shared admission
The unified host SHALL prefer eligible tenants with fewer active invocations and rotate equally active tenants by last service. Exclusive and shared calls SHALL contribute to the same tenant admission policy. Unused capacity SHALL remain borrowable within configured global and tenant limits; running work SHALL NOT be preempted solely because demand changes.

#### Scenario: Idle capacity borrowing
- **WHEN** one customer submits independent plugins while no other customer needs capacity
- **THEN** it can use all available normal capacity within the global budgets

#### Scenario: Competing customer arrives
- **WHEN** another customer queues work while the first customer occupies capacity
- **THEN** the next eligible free slot serves the less-active customer ahead of more work for the saturated customer

### Requirement: Reconstructible residency
The host SHALL retain customer-bound state by default. Only explicitly reconstructible customer-bound workers SHALL be destroyed on scheduling pressure or configured idle eviction. Approved clean unary sessions SHALL release binding residency into the compatible reusable pool. A separate pristine reserve SHALL use only recently requested registered profiles and remain bounded independently of customer count. Shared workers SHALL remain resident until instance disposal or failure. An active stream residency lease SHALL prevent eviction during consumer pauses.

#### Scenario: Cold customer replaces idle customer
- **WHEN** capacity is full of explicitly reconstructible customer-bound workers and a customer without a resident worker is selected
- **THEN** an idle used worker is destroyed before assigning fresh capacity

#### Scenario: State-affine worker under pressure
- **WHEN** another customer needs capacity held by a default customer-bound worker
- **THEN** the host waits or refuses admission under queue policy instead of discarding the worker's state

#### Scenario: Profile demand disappears
- **WHEN** a profile has no remaining registration or its recent demand expires
- **THEN** its pristine target is removed during maintenance

#### Scenario: Paused stream under pressure
- **WHEN** a consumer pauses a stream and another registration needs its worker reservation
- **THEN** the stream's worker remains resident until operation completion, cleanup or failure

### Requirement: Bounded heavy execution
The trusted host SHALL authorize normal or heavy class at binding. The scheduler SHALL bound heavy declarations per tenant, heavy concurrent work globally and per tenant, and active heavy memory reservations to at most half of the shared budget. Normal and heavy unary invocation deadlines SHALL be bounded separately from queue waiting. Stream exchange and total-operation deadlines SHALL be independent of unary deadlines.

#### Scenario: Heavy work cannot consume every slot
- **WHEN** heavy calls reach their shared concurrency limit
- **THEN** further heavy calls wait while eligible normal work can use remaining capacity

#### Scenario: Normal execution expires
- **WHEN** an exclusive normal invocation exceeds its deadline including startup and callbacks
- **THEN** the underlying worker is stopped and the result preserves whether execution may have begun

### Requirement: Bounded queue and shutdown
Queue overflow, queue expiry and cancellation SHALL complete without dispatch. Zero queue wait SHALL select fail-fast admission. Shutdown SHALL close admission, settle pending calls, cancel active work and await cleanup. Cleanup failures SHALL remain observable and close new scheduler admission. Plugin invocations from callback scopes SHALL be denied consistently across exclusive and shared ownership to avoid bounded-pool dependency deadlocks.

#### Scenario: Queue expiry
- **WHEN** a call waits longer than its admission deadline
- **THEN** it completes as busy with no dispatch and queue timing remains observable

#### Scenario: Shutdown with pending work
- **WHEN** the host is disposed with active and queued calls
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
