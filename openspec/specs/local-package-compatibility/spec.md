## Purpose
Make the supported local package, API, protocol and author SDK combinations explicit and reject undeclared compatibility before plugin execution.

## Requirements

### Requirement: Exact declared compatibility
Local installation resolution SHALL require supported host API and protocol levels, an exact host package set and a supported author SDK declaration for every entry point. These identities SHALL be independent of artifact release and application contract identity.

#### Scenario: Supported combination
- **WHEN** an installation declares a supported package/API/protocol/SDK combination and matches the requested application contract
- **THEN** either supported artifact release can resolve without treating its release number as the domain or SDK version

#### Scenario: Unsupported or incomplete combination
- **WHEN** compatibility metadata is missing or any declared API, protocol, package set, author SDK or domain contract is unsupported
- **THEN** resolution is refused before dispatch and activation does not replace the previous selector

### Requirement: Reviewable package surface
The package verification check SHALL compare actual packed metadata and a retained core .NET API baseline, and SHALL report mismatches without updating that baseline automatically.

#### Scenario: Package or surface drift
- **WHEN** packed versions/dependencies or the selected public/protected surface differ from the reviewed baseline
- **THEN** verification fails with evidence identifying the mismatch for review

### Requirement: Preserved recovery identity
Adding compatibility metadata SHALL NOT implicitly migrate existing application pins or rewrite stored effects.

#### Scenario: Incompatible pinned operation
- **WHEN** an existing operation's installation declaration becomes incompatible
- **THEN** recovery fails while its persisted application state remains unchanged
