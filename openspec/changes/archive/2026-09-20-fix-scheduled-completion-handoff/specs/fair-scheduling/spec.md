## ADDED Requirements

### Requirement: Completion releases active scheduling ownership
A scheduled call SHALL release its active scheduling state and signal its idle lifecycle before its public completion task becomes observable as completed. Post-completion lifecycle operations SHALL NOT be rejected solely because the completed call still appears active.

#### Scenario: Immediate lifecycle operation
- **WHEN** a caller awaits a successful scheduled invocation and no other call has been admitted on that registration
- **THEN** the completed call is no longer active and an immediate restart can proceed
