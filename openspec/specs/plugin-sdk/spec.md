# plugin-sdk Specification

## Purpose
Enable plugin authors to implement portable functions and result streams through language SDKs while the host owns transport and customer authority.

## Requirements

### Requirement: Transport-independent multilingual authoring
C#, Python and TypeScript SDKs SHALL let providers register unary functions and asynchronous result streams without writing transport or envelope code. The same built example artifact SHALL run with a local host or a separate worker-host process.

#### Scenario: Unchanged provider deployment
- **WHEN** an SDK example is invoked locally and through the worker-host gateway
- **THEN** the same plugin artifact produces equivalent complete results and authorized callbacks without provider source changes

### Requirement: Bounded result-stream lifecycle
SDK clients SHALL expose incremental results with bounded transport batches, reject oversized items and apply a total stream limit. Cancellation, early consumer exit and worker failure SHALL release stream state or stop the owned worker without reporting partial output as complete success.

#### Scenario: Early consumer exit
- **WHEN** a caller stops enumerating before completion
- **THEN** owned enumeration state is disposed and the binding can serve a subsequent call without exposing the abandoned stream

#### Scenario: Interrupted or oversized output
- **WHEN** production fails, is cancelled or exceeds a configured bound
- **THEN** the caller observes failure or cancellation, rather than successful completion of a truncated stream

#### Scenario: Unresponsive gateway
- **WHEN** a gateway accepts a connection but does not complete a call or stream
- **THEN** the remote client terminates within its configured timeout plus bounded stream cleanup rather than waiting indefinitely

#### Scenario: Repeated binding use with bounded transport retention
- **WHEN** a remote binding serves repeated and concurrent operations within its call budget
- **THEN** completed operations do not accumulate an unbounded number of retained transport sessions, replies remain associated with their caller, and cancelled queued calls are not dispatched

#### Scenario: Transport reuse after abandoned enumeration
- **WHEN** a caller abandons a result stream and invokes another operation on the same binding
- **THEN** the next operation cannot receive leftover items from the abandoned enumeration

### Requirement: Shared host authority
Both SDK deployment paths SHALL use the existing host-bound identity, callback grants and worker lifecycle. Gateway credentials SHALL select a preconfigured binding; request payloads SHALL NOT choose tenant authority or executable paths.

#### Scenario: Foreign authority attempt
- **WHEN** a plugin supplies a foreign tenant value or calls an ungranted host capability, or a gateway caller lacks a valid binding credential
- **THEN** no foreign data is returned and another customer's binding remains usable

#### Scenario: Revoked binding with an open transport
- **WHEN** the trusted host revokes a binding after its transport has been opened
- **THEN** further operations on that transport are denied and another binding remains usable

### Requirement: Complete SDK-path evidence
SDK benchmarks SHALL execute the same language artifacts and complete caller contracts in both topologies, distinguish setup from warm work and record gateway resources separately from plugin worker resources. Evidence SHALL identify tested platforms and trusted-code limitations.

#### Scenario: SDK comparison
- **WHEN** the complete SDK paths are measured
- **THEN** results include correctness, artifact identity, language, topology, payload size and resource scope without describing a loopback test as distributed or sandbox qualification

### Requirement: Author-declared artifact version
Each language SDK SHALL allow the plugin author to declare the artifact version sent at startup, retaining version 1 for existing callers that omit it. The host SHALL continue to reject mismatched artifact versions before invoking plugin functions. Protocol version SHALL remain independent of artifact version.

#### Scenario: Declared version
- **WHEN** an author declares version 2 and the host binds version 2
- **THEN** startup succeeds and ordinary calls execute through the SDK

#### Scenario: Mismatched version
- **WHEN** the declared artifact version differs from the host's expected version
- **THEN** startup fails before a domain function is dispatched

#### Scenario: Existing default and invalid declaration
- **WHEN** an author omits the version or supplies an empty declaration
- **THEN** omission preserves version 1 and an empty declaration is refused

### Requirement: Complete gateway shutdown
Gateway disposal SHALL atomically close new registration and stream admission, revoke all credentials and attempt cleanup of every owned client even when another cleanup fails. Repeated disposal SHALL observe the same completion.

#### Scenario: Multiple cleanup failures
- **WHEN** multiple registered clients fail during disposal
- **THEN** every client is attempted, all credentials are invalid and failures are aggregated

#### Scenario: Registration races shutdown
- **WHEN** registration or stream admission races disposal
- **THEN** it is either admitted before shutdown and included in cleanup or rejected
