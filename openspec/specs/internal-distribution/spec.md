## Purpose
Provide a versioned internal distribution that owners can install and exercise independently of the development checkout.

## Requirements

### Requirement: Identifiable delivery contents
The distribution SHALL identify its own version, component versions, qualified source and checksums. Packaging SHALL refuse candidate artifacts that differ from retained qualification evidence.

#### Scenario: Changed qualified artifact
- **WHEN** a required candidate file differs from its retained digest
- **THEN** packaging fails rather than presenting the changed content as qualified

### Requirement: Independent local installation
On the declared supported runtime combination, the distribution SHALL install its local packages and three examples into a new destination without a source checkout or network package download. Diagnostics SHALL check installed delivery content and required runtimes before launching examples.

#### Scenario: Fresh installation
- **WHEN** an owner installs verified delivery contents into a new destination with matching prerequisites
- **THEN** all three examples can execute their verification checks outside the development checkout and an external consumer can restore the included core packages

#### Scenario: Invalid input or existing destination
- **WHEN** delivery checksums or runtime prerequisites do not match, or the destination already exists
- **THEN** installation is refused without overwriting that destination

### Requirement: Preserved application state
The delivery SHALL separate generated application state from shipped content, refuse incomplete installations for normal launch and provide no implicit migration or clearing of existing recovery evidence.

#### Scenario: Existing state on repeated installation
- **WHEN** installation is attempted again against a used destination
- **THEN** the existing application state and recovery evidence remain unchanged

#### Scenario: Interrupted installation
- **WHEN** installation stops before completion
- **THEN** normal application launch refuses the incomplete installation

### Requirement: Resolvable delivered guidance
Relative documentation links in shipped package readmes and distribution guidance SHALL resolve within the delivered artifact. Repository-only references SHALL be identified as external context.

#### Scenario: Missing documentation target
- **WHEN** packaging or verification encounters a missing relative documentation target
- **THEN** validation fails before accepting the delivery
