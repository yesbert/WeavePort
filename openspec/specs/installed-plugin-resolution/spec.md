## Purpose
Resolve approved local plugin releases to verified content identities that applications can retain across activation and recovery.

## Requirements

### Requirement: Validated installed release
Resolution SHALL validate manifest schema, expected plugin/release/contract, declared entry points, complete bundle content and the declared runtime policy before returning a usable installation. Schema-1 runtime hashes SHALL retain their existing meaning. Portable declarations SHALL validate runtime compatibility and any explicitly requested runtime hashes. Missing, changed or incompatible selected content SHALL fail without selecting another release.

#### Scenario: Invalid installation
- **WHEN** selected metadata is incompatible, a required file is missing or its digest differs
- **THEN** resolution is refused before plugin dispatch

#### Scenario: Legacy runtime replacement
- **WHEN** a schema-1 installation is resolved against a runtime executable with a different digest
- **THEN** resolution is refused even if its runtime version would be compatible

#### Scenario: Portable bundle relocation
- **WHEN** an unchanged portable bundle and manifest are copied between supported hosts with different executable hashes and compatible approved runtimes
- **THEN** resolution succeeds with the same installation identity without resealing

### Requirement: Exact pinned re-resolution
An application SHALL be able to retain an installation identity and require exactly that identity on recovery independently of the active default.

#### Scenario: Activation during an operation
- **WHEN** a new release is activated while a prior operation exists
- **THEN** new operations can select the new release and the prior operation retains its original release and content identity

#### Scenario: Changed pinned manifest
- **WHEN** a persisted operation resolves a manifest that differs from its recorded identity
- **THEN** recovery fails without overwriting the operation's committed state

### Requirement: Explicit trust boundary
The resolver SHALL distinguish integrity against trusted metadata from executable isolation and SHALL document the requirement for stable deployment files throughout execution. Portable runtime compatibility SHALL NOT attest runtime integrity, native dependency portability or equivalent behavior across runtime updates. Runtime integrity and dependencies outside the hashed bundle SHALL remain deployment-owned. Explicit strict executable hashing SHALL remain available without claiming verification of the runtime's dependency closure.

#### Scenario: Installed startup mismatch
- **WHEN** declared installation content launches a worker advertising a different release
- **THEN** startup is rejected by the existing version guard rather than silently accepting the different worker

#### Scenario: Strict portable declaration
- **WHEN** a portable installation explicitly requires a runtime executable digest and the compatible selected runtime has a different digest
- **THEN** resolution refuses the installation

### Requirement: Discover and launch approved installations
The catalog SHALL list verified selected releases by contract from a multi-plugin root, preserving exact pins and release selectors. Runtime declarations SHALL be a verified subset of operator-approved runtimes. Launch metadata and operator approval SHALL determine one effective ownership, memory, degree, timeout and callback policy; incompatible declarations SHALL fail before launch.

#### Scenario: Mixed language root
- **WHEN** a root holds selected Python and Node installations for the same contract
- **THEN** both can be discovered and bound without manually constructing a launch profile, and unapproved shared ownership is rejected

### Requirement: Ecosystem runtime requirements
Portable installations SHALL derive requirements from hashed ecosystem declarations: framework-dependent .NET entry-point runtime configuration, Python project `requires-python`, and Node package `engines.node`. Missing, malformed, conflicting or unsupported declarations SHALL be refused rather than treated as unrestricted. Supported syntax and deployment forms SHALL be documented. Requirements SHALL retain ecosystem-specific semantics instead of treating all ecosystems as a common numeric range.

#### Scenario: Framework compatibility
- **WHEN** a .NET plugin requires Microsoft.NETCore.App 10.0.0 with default Minor roll-forward
- **THEN** compatible installed 10.x frameworks are accepted, lower frameworks and a host with only 11.x are refused, and the same declaration with Major permits compatible 11.x

#### Scenario: Minimum patch and named frameworks
- **WHEN** the selected .NET installation lacks a requested framework or only has a patch below the declared minimum
- **THEN** resolution refuses it even if its SDK or muxer version appears compatible

#### Scenario: Python range
- **WHEN** a Python plugin declares `>=3.11,<4` and the approved interpreter reports 3.12.1
- **THEN** resolution accepts it and refuses an interpreter reporting 3.10.9

#### Scenario: Node range
- **WHEN** a Node plugin declares `>=20 <23` and the approved executable reports v22.1.0
- **THEN** resolution accepts it and refuses v18.20.0

#### Scenario: Declaration disagreement
- **WHEN** recorded runtime requirements disagree with the hashed ecosystem source or required source metadata is absent
- **THEN** resolution refuses the installation before executing plugin code

### Requirement: Bounded runtime validation before launch
Validation SHALL inspect only operator-approved runtime executables, without executing plugin entry points or package lifecycle scripts. Probes SHALL have finite execution and output limits. Failure SHALL identify the runtime alias, expected requirement and observed version or reason observation failed. Initial binding and automatic worker replacement SHALL NOT bypass runtime validation or use selection settings that weaken the sealed requirements.

#### Scenario: Runtime probe failure
- **WHEN** an approved runtime hangs, exits unsuccessfully or returns invalid or oversized version output
- **THEN** validation terminates within its configured bound and refuses the installation with an actionable diagnostic

#### Scenario: Changed runtime before replacement
- **WHEN** a previously resolved installation would start a worker with an incompatible replacement runtime
- **THEN** the new worker is refused before plugin execution

#### Scenario: Unapproved runtime
- **WHEN** an installation names a runtime alias absent from the operator-approved mapping
- **THEN** it is refused without searching PATH or installing a runtime

#### Scenario: Portable recovery
- **WHEN** a pinned portable installation is resolved on another supported host with unchanged manifest and bundle bytes and a compatible runtime
- **THEN** the existing pin is accepted; a changed manifest remains refused without rewriting persisted application state
