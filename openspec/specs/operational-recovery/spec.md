## Purpose

Define fail-closed restart and operator recovery for the trusted native reference deployment without claiming automatic orphan cleanup.

## Requirements

### Requirement: Unclean execution blocks restart
The guarded application SHALL persist an exclusive run marker before starting workers and remove it only after confirmed clean coordinator shutdown. An existing marker, including invalid content, SHALL block new execution without replacing the marker or altering retained booking outcomes.

#### Scenario: Coordinator dies after booking
- **WHEN** the coordinator is killed after a booking commits and before its response
- **THEN** a fresh invocation refuses execution and preserves the original booking and run marker

#### Scenario: Clean run and incomplete cleanup
- **WHEN** a run ends with clean or incomplete coordinator cleanup
- **THEN** its marker is removed only for clean cleanup and a later clean run uses a fresh workspace generation

### Requirement: Explicit supervised recovery
The deployment guide SHALL require verified termination of the old deployment process boundary before archiving an unclean marker and admitting a new run. Recovery SHALL preserve exact stored command and installation identity; old workspace state SHALL never be assigned to new execution. PID-only termination and automatic uncertain-action replay SHALL NOT be presented as safe recovery.

#### Scenario: Supervised fixture recovery
- **WHEN** the isolated test deployment is terminated, its marker is archived and the exact request is repeated
- **THEN** the original booking identity is returned without duplication and a different customer retains independent state

#### Scenario: Unqualified deployment
- **WHEN** descendant containment, filesystem durability or the old deployment's termination cannot be established
- **THEN** the runbook keeps restart gated and identifies the missing qualification instead of claiming automatic recovery
