## Why
The post-merge SonarQube analysis blocks 0.9.0 publication with six maintainability findings.

## What Changes
Remove unused runtime-selection inputs, use dictionary ContainsValue, disambiguate internal signature helpers and reuse test serializer options. Preserve wire fields, public APIs and signature bytes.

## Capabilities
No behavior change. Existing installation and publisher-trust requirements apply.

## Impact
Installation implementation and compatibility tests; no dependency, protocol, package identity or policy change.
