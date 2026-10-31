# Release 0.9.1

Published on 2026-10-31: [GitHub release](https://github.com/yesbert/WeavePort/releases/tag/v0.9.1), from annotated tag `v0.9.1` at `9d3a0d5465da9414467e090529d0476867a139b8`. [Release workflow 41193075806](https://github.com/yesbert/WeavePort/actions/runs/41193075806) qualified and published the original artifacts through the protected NuGet environment.

All seven .NET packages use 0.9.1. Python and TypeScript author SDKs use 0.4.1 and accompany the GitHub release as a wheel and tarball; separate PyPI/npm publication is not claimed. Host API 2 and native protocols 1/2 remain unchanged. Upgrade selected packages together, rebuild/reseal installations offline, re-sign changed manifests and deliberately migrate application pins. Existing deployments retain their original identities.

## Exact-tag qualification

The tagged source passed all 74 stages with 3,599 reported assertion executions and 442 frozen artifact files. [qualification.json](qualification.json) retains the environment, source identity and stage outcomes. Unnumbered checks, including the twelve cancellation ownership cases, also passed; the reported assertion total does not enumerate every assertion.

The frozen artifact inventory SHA-256 is `2e4c8db7a030ee33e4b2d7c49538e1915d64149b7a4b882e275c3749a61bac65`. [release-manifest.json](release-manifest.json) identifies all original package, symbol and author SDK hashes. No rebuilt artifact was substituted after qualification.

## Publication verification

All 16 public GitHub package/symbol/SDK downloads match the qualified bytes exactly; see [GitHub verification](github-publication-check.json). All seven public NuGet package payloads match the qualified contents, allowing only NuGet's added repository signature; see [NuGet verification](publication-check.json). Matching symbols were published by the protected workflow.

The complete [SonarQube report](sonar/summary.md) for the tagged commit records a successful quality gate, zero open issues and zero unreviewed hotspots. [Audit](audit.md) records the dependency workflow, compatibility fixes and review findings. Functional qualification introduces no new sandboxing, capacity or performance guarantee.
