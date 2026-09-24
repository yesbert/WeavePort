## Purpose
Preserve safe failure identity, execution uncertainty and cleanup causes across supported plugin invocation boundaries.

## ADDED Requirements
### Requirement: Safe failure identity
Platform invocation failures SHALL expose stable codes with safe phase and correlation metadata when supplied by the executing boundary. Existing status and execution uncertainty SHALL remain available. Unknown external codes SHALL use a controlled fallback; exception messages SHALL NOT become codes.

#### Scenario: SDK execution failure
- **WHEN** an SDK reports a recognized failure code
- **THEN** the host and clients preserve that code without transmitting exception text

#### Scenario: Transport failure
- **WHEN** gRPC fails without a recognized platform code
- **THEN** the client derives a fixed code from the transport status category independently of its detail text

### Requirement: Primary failure preservation
For author-handler failures followed by registered resource cleanup, and host worker startup or invocation failure followed by teardown, cleanup failures SHALL NOT silently replace the primary failure. Registered SDK cleanup SHALL attempt all owned actions and retain all reported causes. Remote failure metadata SHALL identify cleanup failure without raw exception text.

#### Scenario: Execution and cleanup fail
- **WHEN** execution fails and releasing owned resources also fails
- **THEN** the primary failure remains observable and cleanup failure is separately identifiable

### Requirement: Cancellation ownership
The host SHALL distinguish caller cancellation, host shutdown and expiration of its own deadline. Unrelated cancellation and unexpected internal exceptions SHALL NOT be classified as deadline expiry or malformed protocol solely by their exception type.

#### Scenario: Unrelated cancellation
- **WHEN** a callback raises cancellation without a requested operation token
- **THEN** the outcome identifies an internal failure rather than a timeout
