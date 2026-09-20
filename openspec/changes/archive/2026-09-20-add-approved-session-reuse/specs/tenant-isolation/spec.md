## MODIFIED Requirements

### Requirement: Independent tenant execution
The customer-bound Docker execution profile SHALL isolate worker state, configuration and writable files between tenants using the same artifact version. Trusted local execution SHALL preserve cooperative bound context separation without promising hostile same-user confinement. Explicitly approved session reuse SHALL follow its cooperative cleanup contract rather than claim complete heap or filesystem separation.

#### Scenario: Failure of tenant A
- **WHEN** A crashes, exceeds memory, hangs or is deactivated in the customer-bound Docker profile while B calls the same plugin
- **THEN** B has no induced invocation failures, instance restart or state loss

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
