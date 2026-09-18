# bulk-composition Specification

## Purpose
Enable bounded, request-owned large-result exchange and externally controlled plugin composition with explicit ownership, cancellation and verified caller delivery.

## Requirements

### Requirement: Request-owned large results
The host SHALL expose immutable request-owned result handles without exposing storage paths to plugins, enforce configured byte/object quotas and reject handles from another request scope.

#### Scenario: Foreign reference
- **WHEN** a different request scope attempts to read a result handle
- **THEN** access is denied regardless of whether tenant names match

#### Scenario: Wrong plugin binding
- **WHEN** a product attempts to map a request result through a plugin session bound to a different tenant
- **THEN** composition rejects the operation before dispatch using host-bound session identity

#### Scenario: Failed or cancelled creation
- **WHEN** result production fails, exceeds its quota or is cancelled
- **THEN** partial output is not published and its reservation is released

### Requirement: Bounded external composition
The product SHALL control step bindings and merge semantics, and the composition layer SHALL execute bounded serial and all-required parallel branches without direct plugin-to-plugin communication.

#### Scenario: Parallel failure
- **WHEN** a required branch fails
- **THEN** sibling work is cancelled and all started operations are awaited before failure is returned

#### Scenario: Large result delivery
- **WHEN** a logical result exceeds the invocation frame limit
- **THEN** the host transfers bounded chunks and can stream the complete result to the caller without materializing it in full

### Requirement: Large-result evidence
Benchmarks SHALL identify result sizes, branch topology, concurrency, complete caller delivery, correctness and resource scope separately from BenchmarkDotNet iteration statistics.

#### Scenario: Validate measured output
- **WHEN** a result-list experiment completes
- **THEN** output bytes and integrity are verified and failed or incomplete runs remain distinguishable

### Requirement: Bound SDK composition
Composition SHALL support local and remote SDK clients using identity established by the trusted binding, and SHALL reject another tenant before dispatching plugin work.

#### Scenario: Matching local or remote binding
- **WHEN** an SDK bulk-map operation belongs to the result scope tenant
- **THEN** bounded mapping produces the complete expected result with the same limits in both topologies

#### Scenario: Mismatched or revoked remote binding
- **WHEN** remote binding discovery fails or identifies another tenant
- **THEN** no bulk-map operation is dispatched and no output is committed

### Requirement: Observable composition cleanup failure
Composition SHALL attempt independent cleanup actions and preserve producer and cleanup failures without releasing reservations for output that could not be deleted.

#### Scenario: Failed partial-file removal
- **WHEN** production fails and its partial output cannot be removed
- **THEN** both failures remain observable and reserved capacity remains accounted for until scope cleanup

#### Scenario: Cancellation callback failure
- **WHEN** scope cancellation callbacks throw during disposal
- **THEN** admitted operations are still drained, storage cleanup is attempted and repeated disposal observes the same outcome

### Requirement: Published composition contract
The composition package SHALL have a reviewed API and packed-consumer verification using the exact release artifacts.

#### Scenario: Clean consumer restore
- **WHEN** a consumer restores the qualified composition package with its declared dependencies
- **THEN** it can execute bounded local composition without requiring gateway hosting
