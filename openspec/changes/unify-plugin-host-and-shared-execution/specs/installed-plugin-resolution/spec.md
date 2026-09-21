## ADDED Requirements

### Requirement: Discover and launch approved installations
The catalog SHALL list verified selected releases by contract from a multi-plugin root, preserving exact pins and release selectors. Runtime declarations SHALL be a verified subset of operator-approved runtimes. Launch metadata and operator approval SHALL determine one effective ownership, memory, degree, timeout and callback policy; incompatible declarations SHALL fail before launch.

#### Scenario: Mixed language root
- **WHEN** a root holds selected Python and Node installations for the same contract
- **THEN** both can be discovered and bound without manually constructing a launch profile, and unapproved shared ownership is rejected
