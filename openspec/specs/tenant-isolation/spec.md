## Purpose

Define tested tenant boundaries across Docker and cooperative trusted local execution. Private endpoint mount and memory-exhaustion scenarios apply to the Docker profiles; native process execution is not a hostile-code sandbox.

## Requirements

### Requirement: Independent tenant execution
The customer-bound Docker execution profile SHALL isolate worker state, configuration and writable files between tenants using the same artifact version. Trusted local execution SHALL preserve cooperative bound context separation without promising hostile same-user confinement. Explicitly approved session reuse SHALL follow its cooperative cleanup contract rather than claim complete heap or filesystem separation.

#### Scenario: Failure of tenant A
- **WHEN** A crashes, exceeds memory, hangs or is deactivated in the customer-bound Docker profile while B calls the same plugin
- **THEN** B has no induced invocation failures, instance restart or state loss

### Requirement: Bounded execution
The host SHALL bound frames, callbacks, invocation deadlines, concurrent admissions and resource reservations per execution instance. Hard worker resource ceilings SHALL depend on the selected execution profile and SHALL NOT be claimed for trusted local execution.

#### Scenario: Flood and ignored cancellation
- **WHEN** A floods admission or ignores cancellation
- **THEN** excess work is rejected or terminated without unbounded queuing and B remains callable

### Requirement: Controlled lifecycle
The host SHALL permit isolated restart and disposal and reject calls to disabled bindings.

#### Scenario: Scoped restart
- **WHEN** A restarts its binding
- **THEN** B retains its process and its state

### Requirement: No used-worker reassignment
Customer-bound used execution environments SHALL be destroyed rather than reassigned. Only an operator-approved native deployment with the session-cleanup capability and a successful cleanup acknowledgement SHALL permit compatible sequential reuse. Fresh cooperative state SHALL NOT be presented as a sandbox against hostile same-user code.

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
Nested plugin invocations from a completed or cancelled callback scope SHALL be denied even when the nested target has the same tenant identifier.

#### Scenario: Late nested callback
- **WHEN** a callback resumes after its originating invocation ended and invokes another binding
- **THEN** the host denies the nested invocation without dispatch

### Requirement: Private worker transport endpoints
A worker using the local transport SHALL receive access only to its own invocation endpoint, without a writable shared host directory or Docker control endpoint. Removal uncertainty SHALL retain endpoint ownership and capacity accounting.

#### Scenario: Inspect endpoint exposure
- **WHEN** two customers run the same plugin over local endpoints
- **THEN** each worker can communicate only through its assigned endpoint and cannot create files in its read-only endpoint mount or access the other worker's endpoint

#### Scenario: Fault and replacement
- **WHEN** A fails or its removal cannot be confirmed
- **THEN** B retains its channel and authority, and A's endpoint is not reused for another customer
