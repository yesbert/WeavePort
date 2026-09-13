## Purpose
Allow application owners to diagnose runtime failures without exposing plugin data or configuring global logging from the library.

## Requirements

### Requirement: Optional safe operational events
The host SHALL accept caller-owned optional logging and emit stable event identifiers for startup, callback, invocation, maintenance and cleanup failures. Default events SHALL exclude payloads, configuration, credentials, raw stderr and exception messages.

#### Scenario: Failure with sensitive input
- **WHEN** a failure occurs with secrets in input or exception messages and logging is enabled
- **THEN** the event identifies the failure stage and type without containing those secrets

#### Scenario: Logging omitted
- **WHEN** the caller uses the existing host constructor
- **THEN** the library requires no logging configuration and retains existing invocation behavior

### Requirement: Pending cleanup age
The coordinator snapshot SHALL expose the age in seconds of the oldest still-pending worker removal, measured with the injected operational clock from first quarantine entry. Retries SHALL retain that age; confirmed removal SHALL stop contributing. No pending removal SHALL report zero age. Cleanup reservations and assignment restrictions SHALL remain unchanged.

#### Scenario: Overlapping short removals
- **WHEN** different short worker removals overlap continuously
- **THEN** the reported oldest age follows the oldest currently pending removal rather than the duration of the aggregate nonzero count

#### Scenario: Retried uncertain removal
- **WHEN** removal fails and is retried
- **THEN** its age continues from the original quarantine entry and its capacity remains reserved until confirmed removal

### Requirement: Safe host admission reasons
When caller-owned logging is enabled, host admission refusals SHALL identify the responsible gate using fixed reason codes without payloads, credentials or exception messages. Rejection SHALL retain existing pre-dispatch behavior and limits.

#### Scenario: Simultaneous start ceiling
- **WHEN** all configured worker-start slots are occupied and another cold invocation arrives
- **THEN** it is rejected before dispatch and logging identifies the concurrent-start gate
