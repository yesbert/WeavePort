## Purpose
Provide a reproducible independent application that teaches plugin integration while the application retains domain state and orchestration.

## Requirements

### Requirement: Explainable finite decision
The example SHALL run two configured participants against three proposals, display their evaluations and knowledge inputs, and select a deterministic winner using application-owned contracts and real plugin workers.

#### Scenario: Complete decision
- **WHEN** the default example runs
- **THEN** both participants evaluate all proposals and the application displays a completed result and persists the committed evaluations

#### Scenario: Configuration changes the decision
- **WHEN** the participant priorities are changed from cost-oriented to benefit-oriented
- **THEN** the selected winner changes predictably without changing host code

### Requirement: Portable strategies
C# and Python strategies SHALL accept the same domain inputs and produce equivalent evaluations for the same bound configuration and knowledge.

#### Scenario: Language substitution
- **WHEN** only a participant's language implementation changes
- **THEN** its evaluation and the final decision remain equivalent

### Requirement: Application-owned recovery
The example SHALL resume from committed evaluations after worker replacement, reject incompatible journal configuration, and prevent simultaneous writers to the same journal.

#### Scenario: Worker stops between steps
- **WHEN** a worker is terminated after the first evaluation is committed
- **THEN** a resumed run completes with the same evaluations and winner as an uninterrupted run

#### Scenario: Incompatible resume
- **WHEN** a saved run is opened with different configuration or plugin artifacts
- **THEN** it is refused without overwriting the saved run

### Requirement: Scoped host knowledge
The example SHALL grant knowledge access explicitly and keep concurrent run/profile knowledge and journal data separate.

#### Scenario: Missing grant
- **WHEN** the strategy requests knowledge without its grant
- **THEN** the application reports an unsuccessful decision, persists no evaluation for that call and exposes no knowledge through that callback

#### Scenario: Concurrent profiles
- **WHEN** independent runs use different profiles and customers concurrently
- **THEN** each decision uses only its configured knowledge and each journal contains only its own evaluations

### Requirement: Run-owned plugin version
The example SHALL resolve a concrete installed plugin version for a new run, persist it, and retain it across subsequent calls, worker replacement and resume. Activation SHALL affect only new runs that have not explicitly selected a version.

#### Scenario: Activation while a run is live
- **WHEN** version 2 becomes the default while a version 1 run remains active and a new run starts
- **THEN** the existing run continues using version 1 and the new run uses version 2, with distinguishable version-specific results

#### Scenario: Resume after activation
- **WHEN** an unfinished version 1 journal is resumed after version 2 becomes the default
- **THEN** the remaining evaluations and replay use version 1, including after worker replacement

#### Scenario: Explicit selection and invalid release
- **WHEN** a new run explicitly selects an installed version
- **THEN** that version is used regardless of the default, and an unavailable or invalid selection is refused without falling back

#### Scenario: Pinned artifact no longer matches
- **WHEN** a selected artifact is unavailable or its recorded content has changed
- **THEN** the run is refused rather than continued with another artifact, and its committed journal is retained

#### Scenario: Independent release artifacts
- **WHEN** another release is installed or altered without changing the selected release or shared runtime
- **THEN** an existing run remains resumable using its pinned artifacts

### Requirement: Versioned multilingual equivalence
The example SHALL identify the actual release of its C# and Python workers and preserve equivalent scoring within each release.

#### Scenario: Both release implementations
- **WHEN** a run substitutes C# for Python or Python for C# within the same release
- **THEN** evaluations and final result remain equivalent and each evaluation identifies the selected release
