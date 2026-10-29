## ADDED Requirements

### Requirement: Publisher policy across installation entry points
Offline staging validation, synchronous and asynchronous resolution, discovery, activation and pinned recovery SHALL consistently apply the catalog's configured publisher policy before returning a usable installation or changing a selector. Diagnostic discovery SHALL preserve actionable stable publisher refusal reasons and continue reporting healthy siblings. Existing unsigned callers using integrity-only policy SHALL retain their content validation and pin behavior. Signature metadata SHALL NOT replace complete bundle validation or change the manifest-based installation identity.

#### Scenario: Activation refusal
- **WHEN** required publisher validation fails during activation
- **THEN** the selected release remains unchanged and the caller receives the refusal reason

#### Scenario: Mixed discovery
- **WHEN** diagnostic discovery encounters a valid trusted release, an unsigned release and an untrusted signed release under required policy
- **THEN** it returns one usable installation and two publisher refusals without silently omitting either refusal

#### Scenario: Unsigned compatibility
- **WHEN** an existing unsigned schema-1 or schema-2 installation is resolved using the existing integrity-only policy
- **THEN** existing content, runtime and pin validation behavior is retained

#### Scenario: Required policy on a legacy release
- **WHEN** a schema-1 release is presented under required publisher policy
- **THEN** it is refused with an actionable requirement to migrate offline to signed schema-2 content without rewriting persisted pins automatically

#### Scenario: Mutation after signing
- **WHEN** signed manifest bytes or hashed bundle content change, or an undeclared executable file is added
- **THEN** resolution refuses the installation even when the original signer is trusted
