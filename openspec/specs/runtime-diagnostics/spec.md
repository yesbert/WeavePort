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

### Requirement: Shared worker recovery visibility
The host SHALL expose shared readiness, occupied slots, restart and disabled state. Optional raw plugin diagnostics SHALL require explicit operator opt-in and SHALL bound retained line size and delivery. Default logging SHALL continue to omit raw stderr and tenant data.

#### Scenario: Crash budget exhausted
- **WHEN** a resident shared plugin repeatedly crashes beyond its configured restart budget
- **THEN** its state becomes disabled, pending callers complete boundedly, and default logs contain no raw plugin output

### Requirement: Stable platform exception codes
Public .NET plugin-call and artifact-version-mismatch exceptions SHALL expose a stable programmatic error code without requiring message parsing. Existing status values, exception categories and execution-uncertainty metadata SHALL remain available. SDK cleanup exceptions SHALL identify cleanup-error. Platform-generated codes SHALL contain no payload, configuration or version metadata; diagnostic documentation SHALL describe their meaning and recovery limits. Standard argument, cancellation and transport exceptions are outside this code contract.

#### Scenario: Failed plugin call
- **WHEN** a client operation raises a plugin-call exception
- **THEN** its error code equals the existing operation status and its execution-uncertainty metadata is preserved

#### Scenario: Startup version disagreement
- **WHEN** startup raises an artifact-version-mismatch exception
- **THEN** its error code is version-mismatch independently of expected and advertised version strings

#### Scenario: Cleanup failure
- **WHEN** a registered SDK cleanup action fails
- **THEN** the cleanup exception identifies cleanup-error and retains existing aggregate or cause information

### Requirement: Correlated failure diagnostics
Invocation failure diagnostics SHALL identify a stable code, phase and correlation ID without exception messages by default. Full exception details SHALL require a caller-owned explicit opt-in destination. Failure traces SHALL identify the safe error code. Diagnostic delivery failure SHALL NOT replace an invocation outcome.

#### Scenario: Protected detailed destination
- **WHEN** a caller explicitly configures a detailed exception destination
- **THEN** the destination receives the local exception while default structured logging remains free of its message

#### Scenario: Diagnostic destination fails
- **WHEN** a configured detailed destination throws
- **THEN** the original operation outcome remains available
