## ADDED Requirements

### Requirement: Safe shutdown resource release
Coordinator and binding shutdown SHALL close admission before releasing owned cancellation resources, request cancellation of admitted work and release those resources after operations using them finish. Concurrent and repeated disposal SHALL observe one shutdown outcome. Cleanup failures SHALL remain observable and SHALL NOT skip independent release attempts or erase uncertain worker reservations.

#### Scenario: Worker startup races shutdown
- **WHEN** shutdown begins while an admitted worker is starting
- **THEN** startup is cancelled and accounted for before its owned cancellation resources are released, and no worker is published for subsequent use

#### Scenario: Repeated disposal after a cleanup error
- **WHEN** concurrent disposal callers encounter a throwing cancellation callback or failed worker cleanup
- **THEN** callers observe the same completed shutdown outcome, independent release attempts still run and uncertain worker reservations remain visible

#### Scenario: Another binding remains usable
- **WHEN** tenant A disposes a binding while tenant B uses the same plugin artifact and version
- **THEN** B retains its independent execution and callback authority, and A's detached callbacks retain admission until actual completion

### Requirement: Trusted adapter setup
Native Unix sockets SHALL use exclusively created owner-only temporary directories. Docker execution SHALL use an absolute executable selected by the trusted deployment, without resolving it through the process PATH.

#### Scenario: Private socket allocation
- **WHEN** multiple native Unix endpoints are created
- **THEN** each owns a distinct private directory and releases it when disposed, including before listening

#### Scenario: Docker executable override
- **WHEN** a deployment supplies a relative Docker executable path
- **THEN** setup rejects it before executing any command
