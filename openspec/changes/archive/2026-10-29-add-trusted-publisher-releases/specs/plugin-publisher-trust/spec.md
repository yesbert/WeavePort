## Purpose

Authenticate sealed plugin releases against explicitly configured publisher keys independently of the authority to upload or activate releases.

## ADDED Requirements

### Requirement: Offline publisher signing
A publisher SHALL be able to sign a sealed schema-2 release without executing plugin code, probing runtimes, changing sealed manifest bytes, rewriting installation identity or exposing private key material. Invalid bundle content SHALL be refused before publishing a new signature. Re-signing unchanged content SHALL preserve its installation identity.

#### Scenario: Pipeline signing
- **WHEN** a publisher signs a valid sealed release using its private key
- **THEN** a host with the matching trusted public key can authenticate the exact manifest and complete bundle without access to the private key

#### Scenario: Modified release before signing
- **WHEN** bundle content no longer matches the sealed manifest
- **THEN** signing fails and leaves the previous signature and manifest unchanged

#### Scenario: Resealing signed content
- **WHEN** sealing is requested while a signature sidecar exists
- **THEN** sealing refuses without changing either file and explains the required explicit offline removal before resealing

### Requirement: Explicit publisher trust enforcement
Hosts SHALL be able to require a valid signature from one of multiple configured publisher public keys. Trust SHALL come only from host configuration, never release-supplied keys, network lookup or caller-controlled installation metadata. Required policy with no configured keys SHALL fail configuration validation. A valid signature SHALL NOT bypass content, identity, compatibility or runtime checks.

#### Scenario: Two trusted publishers
- **WHEN** a host trusts keys for two publishers and receives a correctly signed valid release from either
- **THEN** both are eligible for admission, while a release signed by a third publisher is refused

#### Scenario: Forged release with internally consistent hashes
- **WHEN** an installation caller uploads altered code with a freshly recomputed manifest but lacks a trusted publisher private key
- **THEN** required publisher validation refuses the release before any plugin execution or runtime probing

#### Scenario: Missing or malformed signature
- **WHEN** a required signature is absent, invalid, oversized, linked, ambiguously encoded or uses an unsupported format or algorithm
- **THEN** validation refuses it with a stable reason and never falls back to integrity-only acceptance

#### Scenario: Trust configuration isolation
- **WHEN** one tenant or installation caller supplies different trust keys in release content and another host catalog trusts the original publisher
- **THEN** the supplied keys cannot alter either catalog's policy or affect the other catalog's admission decision

### Requirement: Key rotation and withdrawal
Hosts SHALL support overlapping trusted keys for rotation and replacement of a trust snapshot that withdraws a key. Every new verification and pinned resolution under the replacement policy SHALL reject a signature whose key is no longer trusted. Existing installation handles and workers SHALL NOT be described as retroactively revoked; documentation SHALL explain host-owned admission shutdown, binding disposal and re-resolution when immediate revocation is required.

#### Scenario: Withdrawn key with matching pin
- **WHEN** a previously accepted release is resolved with its unchanged pin under a policy that removed its signing key
- **THEN** resolution is refused despite matching content identity

#### Scenario: Rotation without resealing
- **WHEN** an unchanged manifest is re-signed with a newly trusted key during an overlap period
- **THEN** its existing installation pin remains valid and subsequent removal of the old key does not reject the new signature

#### Scenario: Existing worker at withdrawal
- **WHEN** a host replaces its trust snapshot while a binding already owns an installation
- **THEN** the API does not claim to terminate that worker or revoke its existing handle, and immediate revocation requires host-owned disposal and re-resolution

### Requirement: Explicit authenticity limits
Publisher authentication SHALL be documented separately from plugin safety, tenant isolation, runtime integrity and freshness. A configured key SHALL authorize releases within its catalog, without an implied exclusive plugin namespace. Installation credentials SHALL NOT be presented as sufficient authority to change publisher trust.

#### Scenario: Older signed release
- **WHEN** an otherwise valid older release has a signature from a currently trusted key
- **THEN** signature validation alone does not reject it as a rollback; applications retain version-admission policy
