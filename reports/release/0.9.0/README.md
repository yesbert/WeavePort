# WeavePort 0.9.0 release

Published on 2026-10-30 as [v0.9.0](https://github.com/yesbert/WeavePort/releases/tag/v0.9.0), at commit `f94c45d394ac50ca558c6437400a5c15fb3bec38`. The previous release is [0.8.0](../0.8.0/README.md).

This release includes the reflection-free installation JSON fix and opt-in trusted publisher signatures merged in [PR #47](https://github.com/yesbert/WeavePort/pull/47). All seven .NET packages use 0.9.0; Python/TypeScript SDKs remain 0.4.0, host API 2 and native protocols 1/2 remain unchanged.

See [release and migration notes](../../../docs/releases.md#090-trusted-publishers-and-reflection-free-installation-json) for offline resealing, signing, pin migration and trust-policy limits. Existing integrity-only constructors remain available. No new dependency, sandboxing, platform qualification or performance promise is included.

The protected [release workflow](https://github.com/yesbert/WeavePort/actions/runs/41025244479) completed tag qualification, NuGet Trusted Publishing and GitHub release creation. The [prepublication correction](prepublication-correction.md) records six main-only quality findings fixed before tagging, with a clean final SonarQube analysis.

## Historical preparation qualification

Committed candidate `90cd2863e5f21dca55bf22ec26a17a5a68b6a7af` passed all 73 stages with 3,599 reported assertion executions and 442 frozen artifact files. The stage-level record is [preparation-qualification.json](preparation-qualification.json); the count does not include every unnumbered assertion in the reflection-disabled sealing/publisher suites, which also passed in the portable-installations stage.

The artifact inventory SHA-256 is `35763fce047a02cbcec02bb6a06a877baa673a73773934d49db0c3f32a907d2c`. Qualification ran on native macOS arm64 with SDK 10.0.403, Python 3.12.16, Node 26.12.1 and npm 11.20.0. It includes packed consumers, independent signatures, public API/dependency baselines, recovery, all maintained samples and deliberate modified-package refusal.

`python3 scripts/release.py v0.9.0 --candidate <candidate-directory> --output <new-export-directory>` validated metadata/provenance and exported the original seven NuGet packages, seven symbol packages and two author SDK artifacts without rebuilding. [preparation-artifacts.json](preparation-artifacts.json) retains their SHA-256 hashes. The local export is `artifacts/release-0.9.0-preparation`; it is not a public release asset.

The follow-up evidence/archive commit changes only release records and planning artifacts. These preparation bytes are not exact-tag publication evidence. After a reviewed merge, the release workflow must qualify the authorized immutable tag before publishing. See the [preparation audit](audit.md) for scope and limitations.


## Published tag qualification

The exact tagged revision passed all 73 stages with 3,599 reported assertion executions and 442 frozen artifact files. [qualification.json](qualification.json) records its environment and stages. The artifact inventory SHA-256 is `d17919e49d2dca4e167bbb0dc073190828806240c133fc33154450586557404c`. [release-manifest.json](release-manifest.json) identifies the workflow, source commit and exact package/symbol/SDK hashes.

All seven NuGet packages and matching symbols were published through the protected environment using short-lived OIDC credentials. All 16 GitHub package/symbol/SDK downloads match the qualified bytes exactly; see [GitHub verification](github-publication-check.json). All seven public NuGet package payloads match the qualified contents, allowing only NuGet's added repository signature; see [NuGet verification](publication-check.json). No rebuilt package was substituted after qualification.

Python and TypeScript author SDKs remain 0.4.0 and are included as downloads without a claim of separate registry publication. The hosted macOS functional qualification introduces no new platform, Docker containment, capacity or performance guarantee. Preparation records above retain their original source and hashes; they are not the final published artifacts.
