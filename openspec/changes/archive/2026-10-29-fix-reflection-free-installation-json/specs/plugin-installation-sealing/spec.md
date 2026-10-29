## MODIFIED Requirements

### Requirement: Package-contained sealing
A public .NET API SHALL seal an offline built release from its identity, entry points, ecosystem declarations and launch configuration using the package's embedded compatibility matrix. Portable sealing SHALL require neither Python, a source checkout, a consumer-maintained matrix copy nor execution of the target runtime or plugin. The result SHALL include complete bundle hashes and be resolvable by a compatible host with approved compatible runtimes.

#### Scenario: Standalone .NET consumer
- **WHEN** a packed NuGet consumer seals a built plugin without Python or the WeavePort source checkout available
- **THEN** it obtains a valid portable installation with supported package/API/protocol/SDK declarations

#### Scenario: Cross-runtime offline sealing
- **WHEN** a .NET consumer seals Python or Node plugin files and valid ecosystem metadata without those runtimes installed
- **THEN** portable sealing succeeds without executing or downloading a runtime

#### Scenario: Reflection-disabled consumer
- **WHEN** a .NET consumer disables reflection-based JSON serialization and seals a valid .NET, Python or Node release
- **THEN** sealing succeeds without enabling reflection, retains the same manifest bytes and identity as the existing format, and the .NET release can be resolved with its original pin by a compatible host with JSON reflection disabled
