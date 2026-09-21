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

### Requirement: Safe local client shutdown
Local client disposal SHALL reject new operations, cancel outstanding work and attempt owned binding cleanup once. It SHALL release owned cancellation resources safely without requiring a suspended stream consumer to resume enumeration. Concurrent and repeated disposal SHALL observe the same cleanup outcome; late enumeration SHALL observe cancellation without dispatching further plugin work.

#### Scenario: Active local call during disposal
- **WHEN** a local call races disposal
- **THEN** it is either admitted before shutdown and cancelled or refused without dispatch, without accessing a disposed cancellation source

#### Scenario: Suspended stream consumer
- **WHEN** disposal starts while a consumer is suspended after receiving a stream item
- **THEN** owned binding cleanup can complete without consumer resumption and later enumeration observes cancellation without further dispatch

#### Scenario: Binding cleanup throws
- **WHEN** the owned binding fails during disposal
- **THEN** the failure remains observable to repeated disposal callers and independent cancellation resource release is still attempted

### Requirement: Separate gateway client installation
Gateway client consumers SHALL be able to install the remote client without an ASP.NET Core shared-framework requirement, while the server package SHALL provide documented service registration and bounded message handling.

#### Scenario: Client-only application
- **WHEN** a plain .NET application references only the remote client package
- **THEN** it can call an authenticated gateway without installing the server package or ASP.NET Core shared framework

### Requirement: Authenticated binding discovery
A gateway SHALL report immutable trusted registration identity only after credential authorization. Remote bound clients SHALL reject incompatible discovery protocol versions.

#### Scenario: Discover identity without plugin execution
- **WHEN** a valid credential requests binding identity
- **THEN** the gateway returns its registered tenant and protocol identity without dispatching a plugin operation

#### Scenario: Revoked credential discovery
- **WHEN** a revoked or unknown credential requests identity
- **THEN** access is denied without exposing another binding

### Requirement: Direct HTTPS gateway deployment
Gateway packages SHALL support direct HTTP/2 TLS deployment with certificate validation, bounded operation deadlines and observable interrupted outcomes. Documentation SHALL distinguish executed topology evidence from unqualified deployment variations.

#### Scenario: Valid and invalid server certificates
- **WHEN** a client connects to a server whose certificate is trusted and matches its hostname
- **THEN** authorized calls and streams complete
- **AND WHEN** certificate trust or hostname validation fails
- **THEN** the connection fails before plugin dispatch

#### Scenario: Connection interruption and restart
- **WHEN** a server connection fails during work and the server later becomes available
- **THEN** the interrupted operation fails without automatic replay and a subsequent explicit operation can reconnect

#### Scenario: Active credential revocation
- **WHEN** the host revokes a binding during a stream
- **THEN** its work is cancelled and another tenant binding remains usable

### Requirement: Qualified gateway packages
Gateway packages SHALL include reviewed API and protocol surfaces, dependency/license evidence, and packed-consumer tests of the exact release artifacts.

#### Scenario: Frozen package export
- **WHEN** gateway release artifacts are exported
- **THEN** their identities and hashes match the artifacts exercised by the successful consumer qualification

### Requirement: Registered session resources
C#, Python and TypeScript SDKs SHALL expose resource ownership and sync/async cleanup registration for one function invocation or complete result stream. Cleanup SHALL revoke context callbacks and registration first, attempt all actions in reverse order, release context references and report failure rather than acknowledge clean reuse when an action fails.

#### Scenario: Customer-owned resources
- **WHEN** a handler returns or throws after registering resources
- **THEN** registered cleanup is attempted and old contexts reject further use

#### Scenario: Stream lifetime
- **WHEN** a result stream spans multiple wire exchanges
- **THEN** its context remains valid for that stream and no reusable acknowledgement is emitted until stream closure and cleanup succeed

### Requirement: Additive cleanup capability
Updated SDKs SHALL advertise native cleanup capability revision one and include boolean reuse readiness in successful results. These fields SHALL preserve default legacy customer-bound host behavior and SHALL NOT allow a plugin to authorize its own cross-customer sharing.

#### Scenario: Operator did not approve
- **WHEN** an updated SDK reports clean completion under a customer-bound profile
- **THEN** the host retains exclusive binding ownership rather than sharing the worker

### Requirement: Concurrent authoring and live stream delivery
All three author SDKs SHALL support approved concurrent unary execution with isolated contexts and cleanup, correlated callbacks, atomic frames and cooperative per-invocation cancellation. Concurrent workers SHALL use a protocol revision rejected by serial-only hosts. Exclusive result streams SHALL flush available items promptly and permit empty unfinished heartbeat batches. Stream exchange and total deadlines SHALL be configured together separately from unary deadlines. Callback count SHALL be configurable with a default of eight.

#### Scenario: Slow stream and bounded callbacks
- **WHEN** a stream first yields after twelve seconds under a sufficient stream deadline and the same binding has a five-second unary deadline
- **THEN** the stream completes, a hung unary call times out at five seconds, and an explicitly approved callback budget of 64 allows forty callbacks while the default rejects the ninth
