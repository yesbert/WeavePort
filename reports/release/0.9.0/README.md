# WeavePort 0.9.0 preparation

Status: release preparation qualified. Not tagged or published. The latest published release remains [0.8.0](../0.8.0/README.md).

This release includes the reflection-free installation JSON fix and opt-in trusted publisher signatures merged in [PR #47](https://github.com/yesbert/WeavePort/pull/47). All seven .NET packages use 0.9.0; Python/TypeScript SDKs remain 0.4.0, host API 2 and native protocols 1/2 remain unchanged.

See [release and migration notes](../../../docs/releases.md#090-trusted-publishers-and-reflection-free-installation-json) for offline resealing, signing, pin migration and trust-policy limits. Existing integrity-only constructors remain available. No new dependency, sandboxing, platform qualification or performance promise is included.

Publication is outside this preparation. After review and merge, separately authorize tagging and the protected release workflow; retain exact-tag qualification and verify public downloads before announcing availability.

## Qualification

Committed candidate `90cd2863e5f21dca55bf22ec26a17a5a68b6a7af` passed all 73 stages with 3,599 reported assertion executions and 442 frozen artifact files. The stage-level record is [preparation-qualification.json](preparation-qualification.json); the count does not include every unnumbered assertion in the reflection-disabled sealing/publisher suites, which also passed in the portable-installations stage.

The artifact inventory SHA-256 is `35763fce047a02cbcec02bb6a06a877baa673a73773934d49db0c3f32a907d2c`. Qualification ran on native macOS arm64 with SDK 10.0.403, Python 3.12.16, Node 26.12.1 and npm 11.20.0. It includes packed consumers, independent signatures, public API/dependency baselines, recovery, all maintained samples and deliberate modified-package refusal.

`python3 scripts/release.py v0.9.0 --candidate <candidate-directory> --output <new-export-directory>` validated metadata/provenance and exported the original seven NuGet packages, seven symbol packages and two author SDK artifacts without rebuilding. [preparation-artifacts.json](preparation-artifacts.json) retains their SHA-256 hashes. The local export is `artifacts/release-0.9.0-preparation`; it is not a public release asset.

The follow-up evidence/archive commit changes only release records and planning artifacts. These preparation bytes are not exact-tag publication evidence. After a reviewed merge, the release workflow must qualify the authorized immutable tag before publishing. See the [preparation audit](audit.md) for scope and limitations.
