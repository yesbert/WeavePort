## ADDED Requirements

### Requirement: Registered session resources
C#, Python and TypeScript SDKs SHALL expose resource ownership and sync/async cleanup registration for one function invocation or complete result stream. Cleanup SHALL revoke context callbacks and registration first, attempt all actions in reverse order, release context references and report failure rather than acknowledge clean reuse when an action fails.

#### Scenario: Customer-owned resources
- **WHEN** a handler returns or throws after registering resources
- **THEN** registered cleanup is attempted and old contexts reject further use

#### Scenario: Stream lifetime
- **WHEN** a result stream spans multiple wire exchanges
- **THEN** its context remains valid for that stream and no reusable acknowledgement is emitted until stream closure and cleanup succeed

### Requirement: Additive cleanup capability
Updated SDKs SHALL advertise native cleanup capability revision one and include boolean reuse readiness in successful results. These fields SHALL preserve default legacy customer-bound host behavior and SHALL NOT allow a plugin to authorize its own cross-customer sharing.

#### Scenario: Operator did not approve
- **WHEN** an updated SDK reports clean completion under a customer-bound profile
- **THEN** the host retains exclusive binding ownership rather than sharing the worker
