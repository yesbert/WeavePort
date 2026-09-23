## ADDED Requirements

### Requirement: Explicit assembly version selection
The C# author SDK SHALL support explicit selection of the entry or supplied assembly informational version, removing build metadata by default while preserving prerelease labels. Missing metadata SHALL fail explicitly. Existing callers SHALL retain default version 1. Startup mismatch SHALL report expected and advertised versions to the authorized caller without dispatch or raw default logging.

#### Scenario: Installed release mismatch
- **WHEN** installation version 1.0.0 starts a worker advertising 1
- **THEN** failure includes both versions, reports no dispatch, and the domain function does not execute

#### Scenario: Assembly build metadata
- **WHEN** an author selects assembly version 1.2.3-beta+commit
- **THEN** the default helper returns 1.2.3-beta and explicit metadata retention returns the full value

### Requirement: Per-call client timing
Typed and untyped clients SHALL offer an immutable result containing the value and nullable host elapsed milliseconds for that call. Local and gateway clients SHALL preserve host timing without substituting transport timing; clients or older gateways without timing SHALL return unavailable metadata. Existing value-only calls and failure outcome semantics SHALL remain compatible.

#### Scenario: Concurrent calls
- **WHEN** concurrent calls complete out of order
- **THEN** each result retains its own value and elapsed metadata

#### Scenario: Legacy implementation
- **WHEN** an existing custom client or older gateway supplies only a value
- **THEN** the metadata API returns that value with unavailable elapsed timing
