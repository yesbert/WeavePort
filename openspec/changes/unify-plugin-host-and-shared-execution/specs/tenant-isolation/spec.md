## ADDED Requirements

### Requirement: Explicit simultaneous trusted sharing
Only explicitly approved shared execution SHALL place multiple tenants in one worker simultaneously. Shared invocation callbacks SHALL retain their own immutable authority and expired scopes SHALL refuse further callbacks. Shared process failure SHALL be documented and reported as affecting all in-flight invocations, without extending that behavior to exclusive ownership.

#### Scenario: Concurrent callback identities
- **WHEN** two tenants interleave callbacks on a shared worker
- **THEN** each callback is authorized against its originating invocation and cannot select another tenant through plugin-supplied payload

## MODIFIED Requirements

### Requirement: Independent tenant execution
The customer-bound Docker execution profile SHALL isolate worker state, configuration and writable files between tenants using the same artifact version. Trusted local execution SHALL preserve cooperative bound context separation without promising hostile same-user confinement. Explicitly approved session reuse SHALL follow its cooperative cleanup contract rather than claim complete heap or filesystem separation. Explicit Shared ownership SHALL preserve per-invocation authority while accepting shared memory and a shared process-failure boundary.

#### Scenario: Failure of tenant A
- **WHEN** A crashes, exceeds memory, hangs or is deactivated in the customer-bound Docker profile while B calls the same plugin
- **THEN** B has no induced invocation failures, instance restart or state loss

#### Scenario: Shared process failure
- **WHEN** a shared worker process fails with invocations from A and B in flight
- **THEN** both invocations may fail without automatic replay and their results preserve possible execution

### Requirement: Controlled lifecycle
The host SHALL permit isolated restart and disposal of exclusive bindings and reject calls to disabled bindings. Disposing a shared tenant view SHALL revoke that view without stopping unrelated views. Restart and disposal of the shared plugin instance SHALL belong to the shared instance owner, not to an individual tenant view.

#### Scenario: Scoped restart
- **WHEN** A restarts its exclusive binding
- **THEN** B retains its process and its state

#### Scenario: Shared view disposal
- **WHEN** A disposes its shared tenant view while B uses another view
- **THEN** B remains callable and the shared worker is not stopped solely because A disposed its view

### Requirement: No used-worker reassignment
Customer-bound used execution environments SHALL be destroyed rather than reassigned. Only an operator-approved native deployment with the session-cleanup capability and a successful cleanup acknowledgement SHALL permit compatible sequential reuse. Explicit Shared ownership SHALL use resident workers under its distinct simultaneous-sharing contract rather than claim sequential cleanup between invocations. Fresh cooperative state SHALL NOT be presented as a sandbox against hostile same-user code.

#### Scenario: Customer replacement
- **WHEN** customer-bound fixture A writes private markers and releases its instance before B uses the identical plugin
- **THEN** B receives a fresh environment and callbacks use only B's bound authority

#### Scenario: Approved customer switch
- **WHEN** an approved SDK function completes registered cleanup and another compatible customer invokes it
- **THEN** the same worker may execute with the new bound context and grants while old SDK contexts reject callbacks

#### Scenario: Hidden global negative control
- **WHEN** approved code retains an unregistered customer value in global state
- **THEN** documentation and tests expose the accepted residual risk rather than claiming automatic erasure

### Requirement: Expired callback scope
Nested plugin invocations from any callback scope SHALL be denied, including active, completed or cancelled scopes and targets with the same tenant identifier, to prevent bounded-pool dependency deadlocks.

#### Scenario: Late nested callback
- **WHEN** a callback resumes after its originating invocation ended and invokes another binding
- **THEN** the host denies the nested invocation without dispatch

#### Scenario: Active nested callback
- **WHEN** an active callback invokes another exclusive or shared binding
- **THEN** the host denies the nested invocation without dispatch
