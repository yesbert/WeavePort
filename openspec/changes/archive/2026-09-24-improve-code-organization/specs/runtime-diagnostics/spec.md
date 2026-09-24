## ADDED Requirements

### Requirement: Stable platform exception codes
Public .NET plugin-call and artifact-version-mismatch exceptions SHALL expose a stable programmatic error code without requiring message parsing. Existing status values, exception categories and execution-uncertainty metadata SHALL remain available. SDK cleanup exceptions SHALL identify cleanup-error. Platform-generated codes SHALL contain no payload, configuration or version metadata; diagnostic documentation SHALL describe their meaning and recovery limits. Standard argument, cancellation and transport exceptions are outside this code contract.

#### Scenario: Failed plugin call
- **WHEN** a client operation raises a plugin-call exception
- **THEN** its error code equals the existing operation status and its execution-uncertainty metadata is preserved

#### Scenario: Startup version disagreement
- **WHEN** startup raises an artifact-version-mismatch exception
- **THEN** its error code is version-mismatch independently of expected and advertised version strings

#### Scenario: Cleanup failure
- **WHEN** a registered SDK cleanup action fails
- **THEN** the cleanup exception identifies cleanup-error and retains existing aggregate or cause information
