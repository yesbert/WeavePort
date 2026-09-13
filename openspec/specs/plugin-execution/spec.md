## Purpose

Provide observable PoC evidence and contracts for plugin execution in an independent plugin platform.

## Requirements

### Requirement: Packaged multilingual contracts
A consumer SHALL invoke C#, Python and TypeScript implementations through the same packaged host contract.

#### Scenario: Provider replacement
- **WHEN** a consumer invokes the search contract against each language
- **THEN** each result satisfies the same contract without consumer code changes

### Requirement: Host-owned authority
Callbacks SHALL use immutable host-bound tenant identity and explicitly granted capabilities, with bounded calls and cancellation.

#### Scenario: Forged callback context
- **WHEN** a plugin requests another tenant or an ungranted operation
- **THEN** the host refuses unauthorized access and exposes no foreign data

### Requirement: Stable context and recovery
Bindings SHALL retain version and profile selection, while applications own resumable state and external action semantics.

#### Scenario: Restart and replay
- **WHEN** a worker restarts and an application resubmits persisted state or an idempotent action
- **THEN** state processing can continue and an uncertain action is not silently classified as a definite failure

### Requirement: Explicit local transport selection
A trusted Linux deployment SHALL be able to select a private local transport while preserving the packaged C#, Python and TypeScript invocation and callback contracts. An incompatible transport configuration or worker SHALL fail boundedly without silent fallback.

#### Scenario: Equivalent operations
- **WHEN** the same supported plugin is invoked through either configured transport
- **THEN** its successful results, bound context and authorized callbacks satisfy the same contract

#### Scenario: Invalid local endpoint
- **WHEN** the configured local transport cannot establish a compatible worker channel
- **THEN** startup fails within its deadline and owned resources are removed or remain explicitly quarantined

### Requirement: Unambiguous protocol envelopes
The host SHALL reject repeated reserved fields in worker protocol envelopes, including escaped spellings of the same name, and SHALL reject unsupported or non-integral ready versions as protocol failures. Plugin payload and result contents SHALL remain application-owned JSON.

#### Scenario: Ambiguous identity
- **WHEN** a worker repeats a reserved envelope field such as invocation identity, operation or payload
- **THEN** the host terminates that worker invocation with a protocol error before executing a callback from that envelope

#### Scenario: Malformed ready version
- **WHEN** a worker advertises a fractional, out-of-range or wrongly typed protocol version
- **THEN** startup fails as a bounded protocol error and its worker is cleaned up without disrupting another customer

#### Scenario: Opaque result contents
- **WHEN** a valid result envelope contains application-owned JSON
- **THEN** envelope validation does not interpret its nested fields as host authority

### Requirement: Explicit trusted local execution
A consumer SHALL be able to select local process execution without Docker for the supported C#, Python and TypeScript fixtures. Local execution SHALL require explicit trusted-code acknowledgement and SHALL not be advertised as a hostile-code sandbox.

#### Scenario: Docker unavailable
- **WHEN** a trusted local fixture is invoked with Docker unavailable to the consuming application
- **THEN** invocation and authorized callbacks complete without requiring Docker

#### Scenario: Required protection unavailable
- **WHEN** a consumer requires filesystem or network confinement or hard worker resource ceilings that the selected adapter does not provide
- **THEN** binding and prewarming are rejected before launching a worker, without fallback

### Requirement: Explicit native channel selection
Trusted local consumers SHALL be able to select a private Unix-socket channel for the supported native fixtures without requiring Docker. Channel selection SHALL preserve bound authority, frame limits and fresh worker ownership, and SHALL NOT add a sandbox guarantee.

#### Scenario: Equivalent native channels
- **WHEN** a supported trusted fixture runs with either native channel
- **THEN** contracts, authorized callbacks, crash/cancellation recovery and customer-local state satisfy the same binding contract

#### Scenario: Incompatible native channel
- **WHEN** the selected channel cannot establish a compatible worker connection
- **THEN** startup fails boundedly, owned endpoints are removed or remain accounted for, and no fallback occurs
