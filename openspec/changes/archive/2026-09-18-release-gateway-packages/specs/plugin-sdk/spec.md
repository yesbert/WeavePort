## ADDED Requirements

### Requirement: Separate gateway client installation
Gateway client consumers SHALL be able to install the remote client without an ASP.NET Core shared-framework requirement, while the server package SHALL provide documented service registration and bounded message handling.

#### Scenario: Client-only application
- **WHEN** a plain .NET application references only the remote client package
- **THEN** it can call an authenticated gateway without installing the server package or ASP.NET Core shared framework

### Requirement: Authenticated binding discovery
A gateway SHALL report immutable trusted registration identity only after credential authorization. Remote bound clients SHALL reject incompatible discovery protocol versions.

#### Scenario: Discover identity without plugin execution
- **WHEN** a valid credential requests binding identity
- **THEN** the gateway returns its registered tenant and protocol identity without dispatching a plugin operation

#### Scenario: Revoked credential discovery
- **WHEN** a revoked or unknown credential requests identity
- **THEN** access is denied without exposing another binding

### Requirement: Direct HTTPS gateway deployment
Gateway packages SHALL support direct HTTP/2 TLS deployment with certificate validation, bounded operation deadlines and observable interrupted outcomes. Documentation SHALL distinguish executed topology evidence from unqualified deployment variations.

#### Scenario: Valid and invalid server certificates
- **WHEN** a client connects to a server whose certificate is trusted and matches its hostname
- **THEN** authorized calls and streams complete
- **AND WHEN** certificate trust or hostname validation fails
- **THEN** the connection fails before plugin dispatch

#### Scenario: Connection interruption and restart
- **WHEN** a server connection fails during work and the server later becomes available
- **THEN** the interrupted operation fails without automatic replay and a subsequent explicit operation can reconnect

#### Scenario: Active credential revocation
- **WHEN** the host revokes a binding during a stream
- **THEN** its work is cancelled and another tenant binding remains usable

### Requirement: Qualified gateway packages
Gateway packages SHALL include reviewed API and protocol surfaces, dependency/license evidence, and packed-consumer tests of the exact release artifacts.

#### Scenario: Frozen package export
- **WHEN** gateway release artifacts are exported
- **THEN** their identities and hashes match the artifacts exercised by the successful consumer qualification
