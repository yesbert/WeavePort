## ADDED Requirements
### Requirement: Correlated failure diagnostics
Invocation failure diagnostics SHALL identify a stable code, phase and correlation ID without exception messages by default. Full exception details SHALL require a caller-owned explicit opt-in destination. Failure traces SHALL identify the safe error code. Diagnostic delivery failure SHALL NOT replace an invocation outcome.

#### Scenario: Protected detailed destination
- **WHEN** a caller explicitly configures a detailed exception destination
- **THEN** the destination receives the local exception while default structured logging remains free of its message

#### Scenario: Diagnostic destination fails
- **WHEN** a configured detailed destination throws
- **THEN** the original operation outcome remains available
