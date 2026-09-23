## Purpose

Allow .NET consumers to create portable, integrity-checked plugin installation metadata using the compatibility policy shipped in their WeavePort package.

## Requirements

### Requirement: Package-contained sealing
A public .NET API SHALL seal an offline built release from its identity, entry points, ecosystem declarations and launch configuration using the package's embedded compatibility matrix. Portable sealing SHALL require neither Python, a source checkout, a consumer-maintained matrix copy nor execution of the target runtime or plugin. The result SHALL include complete bundle hashes and be resolvable by a compatible host with approved compatible runtimes.

#### Scenario: Standalone .NET consumer
- **WHEN** a packed NuGet consumer seals a built plugin without Python or the WeavePort source checkout available
- **THEN** it obtains a valid portable installation with supported package/API/protocol/SDK declarations

#### Scenario: Cross-runtime offline sealing
- **WHEN** a .NET consumer seals Python or Node plugin files and valid ecosystem metadata without those runtimes installed
- **THEN** portable sealing succeeds without executing or downloading a runtime

### Requirement: Safe deterministic manifest generation
Sealing SHALL use the same path, link, inventory and metadata constraints as resolution, hash declaration files as bundle content, and produce identical manifest bytes for identical input bytes and options regardless of absolute release path. It SHALL atomically replace only the manifest after validation succeeds, without deleting bundle files, changing selectors or migrating pins. Missing, dynamic-only or unsupported runtime requirements SHALL be rejected with a source-specific diagnostic. Strict runtime hashes SHALL require explicit local runtime inputs.

#### Scenario: Repeatable release
- **WHEN** identical release content and sealing options are supplied in two different directories
- **THEN** portable sealing produces the same manifest digest

#### Scenario: Invalid input preserves prior installation
- **WHEN** sealing encounters a linked file, invalid declaration or invalid launch configuration
- **THEN** it reports the cause and leaves the previous manifest and bundle unchanged

#### Scenario: Source requirement is integrity protected
- **WHEN** a runtime declaration file is modified after sealing
- **THEN** installation resolution refuses the changed bundle

#### Scenario: Explicit offline migration
- **WHEN** a schema-1 release is explicitly resealed as a portable release
- **THEN** a new manifest identity is produced and any previous persisted pin remains unchanged
