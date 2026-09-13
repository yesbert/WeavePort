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
