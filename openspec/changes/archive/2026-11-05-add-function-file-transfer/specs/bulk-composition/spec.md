## ADDED Requirements

### Requirement: Result scope as function output destination
`ResultScope` SHALL be usable as the destination for function-call outputs. Object and scope quotas SHALL apply while the blocks arrive. An output SHALL become a visible result only when the host commits it, and SHALL be deleted when the host aborts it, with its quota reservation released.

#### Scenario: Failed call into a result scope
- **WHEN** a function writes an output into a result scope and the call then fails
- **THEN** no result handle for that output becomes visible, and its partial file and reservation are removed
