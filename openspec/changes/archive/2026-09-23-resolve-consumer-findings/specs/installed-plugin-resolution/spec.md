## ADDED Requirements

### Requirement: Complete diagnostic discovery
The catalog SHALL offer diagnostic enumeration of immediate plugin directories, returning either a verified selected installation or an actionable refusal per directory. Missing selectors, invalid content, incompatible contracts and unapproved runtimes SHALL be visible without preventing valid siblings from being reported. Asynchronous enumeration SHALL propagate cancellation, and root enumeration failures SHALL remain observable.

#### Scenario: Mixed valid and invalid directories
- **WHEN** a catalog contains a valid installation, a directory without active.txt and a malformed selected installation
- **THEN** diagnostic discovery returns three entries with one installation and two refusals while existing filtered discovery retains its contract

#### Scenario: Contract mismatch
- **WHEN** diagnostic discovery specifies an expected contract and a selected installation declares another
- **THEN** the directory is reported as refused rather than silently omitted
