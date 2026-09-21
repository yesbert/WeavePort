## Purpose
Resolve approved local plugin releases to verified content identities that applications can retain across activation and recovery.

## Requirements

### Requirement: Validated installed release
Resolution SHALL validate manifest schema, expected plugin/release/contract, declared entry points and bundle/runtime content before returning a usable installation. Missing or changed selected content SHALL fail without selecting another release.

#### Scenario: Invalid installation
- **WHEN** selected metadata is incompatible, a required file is missing or its digest differs
- **THEN** resolution is refused before plugin dispatch

### Requirement: Exact pinned re-resolution
An application SHALL be able to retain an installation identity and require exactly that identity on recovery independently of the active default.

#### Scenario: Activation during an operation
- **WHEN** a new release is activated while a prior operation exists
- **THEN** new operations can select the new release and the prior operation retains its original release and content identity

#### Scenario: Changed pinned manifest
- **WHEN** a persisted operation resolves a manifest that differs from its recorded identity
- **THEN** recovery fails without overwriting the operation's committed state

### Requirement: Explicit trust boundary
The resolver SHALL distinguish integrity against trusted metadata from executable isolation and SHALL document the requirement for stable deployment files throughout execution.

#### Scenario: Installed startup mismatch
- **WHEN** declared installation content launches a worker advertising a different release
- **THEN** startup is rejected by the existing version guard rather than silently accepting the different worker

### Requirement: Discover and launch approved installations
The catalog SHALL list verified selected releases by contract from a multi-plugin root, preserving exact pins and release selectors. Runtime declarations SHALL be a verified subset of operator-approved runtimes. Launch metadata and operator approval SHALL determine one effective ownership, memory, degree, timeout and callback policy; incompatible declarations SHALL fail before launch.

#### Scenario: Mixed language root
- **WHEN** a root holds selected Python and Node installations for the same contract
- **THEN** both can be discovered and bound without manually constructing a launch profile, and unapproved shared ownership is rejected
