## MODIFIED Requirements

### Requirement: Host-owned authority
Callbacks SHALL use immutable host-supplied invocation identity and explicitly granted capabilities. Exclusive bindings retain one tenant; shared views SHALL supply tenant identity per invocation without taking authority from plugin payloads. Callback count and cancellation SHALL remain bounded.

#### Scenario: Forged callback context
- **WHEN** a plugin requests another tenant or an ungranted operation
- **THEN** the host refuses unauthorized access and exposes no foreign data
