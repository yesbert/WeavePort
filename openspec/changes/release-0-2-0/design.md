## Decisions

Use 0.2.0 for the intentionally breaking pre-1.0 host API migration from 0.1.0; retain MIT and the existing four-package allowlist. Preserve host compatibility level and wire protocol 1 because runtime messages are unchanged; exact package declarations distinguish the new delivery. Python/TypeScript SDKs remain 0.1.0. Historical reports and offline bundles retain their original versions.

All main CI/CodeQL/Sonar checks passed before preparation. Windows capacity measurements and Windows/Linux public-package installation qualification remain documented limits, not expanded claims. The release must pass fresh isolated qualification and package hash/provenance checks, then the normal nuget-org environment gate and NuGet Trusted Publishing. No legacy API-key fallback or tag movement.

Reference: https://semver.org/ — initial development uses 0.y.z; this release makes the incompatible development API change explicit through a minor increment.
