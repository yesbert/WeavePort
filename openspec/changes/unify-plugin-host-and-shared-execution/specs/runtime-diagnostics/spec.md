## ADDED Requirements

### Requirement: Shared worker recovery visibility
The host SHALL expose shared readiness, occupied slots, restart and disabled state. Optional raw plugin diagnostics SHALL require explicit operator opt-in and SHALL bound retained line size and delivery. Default logging SHALL continue to omit raw stderr and tenant data.

#### Scenario: Crash budget exhausted
- **WHEN** a resident shared plugin repeatedly crashes beyond its configured restart budget
- **THEN** its state becomes disabled, pending callers complete boundedly, and default logs contain no raw plugin output
