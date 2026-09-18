## ADDED Requirements

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
