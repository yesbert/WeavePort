## Decisions

Use 0.2.0 for the intentionally breaking pre-1.0 host API migration from 0.1.0; retain MIT and the existing four-package allowlist. Preserve host compatibility level and wire protocol 1 because runtime messages are unchanged; exact package declarations distinguish the new delivery. Python/TypeScript SDKs remain 0.1.0. Historical reports and offline bundles retain their original versions.

All main CI/CodeQL/Sonar checks passed before preparation. Windows capacity measurements and Windows/Linux public-package installation qualification remain documented limits, not expanded claims. The release must pass fresh isolated qualification and package hash/provenance checks, then the normal nuget-org environment gate and NuGet Trusted Publishing. No legacy API-key fallback or tag movement.

Reference: https://semver.org/ — initial development uses 0.y.z; this release makes the incompatible development API change explicit through a minor increment.

## Verified publication

PR #14 passed all required checks and merged as 1f4d25ef23dc4812a2e3bec3131af5fb54e5072b. Annotated v0.2.0 points to that commit. Release run 34851207576 qualified the exact tag (874 assertions, 139 frozen files), exported original tested packages/symbols, passed the normal nuget-org approval and published all four packages and symbols through OIDC. GitHub announcement succeeded. Public NuGet downloads were compared entry-by-entry against tested packages, excluding the added repository signature; all payloads match. Durable release assets and reports/release/0.2.0 preserve hashes, qualification and publication evidence. Main Sonar for the released commit passed.
