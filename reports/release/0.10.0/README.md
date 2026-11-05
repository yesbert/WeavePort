# Release 0.10.0

Published on 2026-11-05: [GitHub release](https://github.com/yesbert/WeavePort/releases/tag/v0.10.0), from annotated tag `v0.10.0` at `c22e7abafb21a46872241b8fb5466d739ed65017`. [Release workflow 42040274242](https://github.com/yesbert/WeavePort/actions/runs/42040274242) qualified the tagged source and published the original artifacts through the protected NuGet environment after maintainer approval.

All seven .NET packages use 0.10.0. Python and TypeScript author SDKs use 0.5.0 and accompany the GitHub release as a wheel and a tarball; separate PyPI/npm publication is not claimed. The release adds [function files](../../../docs/function-files.md): host-supplied file inputs and transactional file outputs for plugin function calls, locally, through the scheduler and through the gateway. Host API 2 and native protocols 1/2 remain unchanged. Upgrade selected packages together, rebuild/reseal installations offline, re-sign changed manifests and deliberately migrate application pins; see the [migration notes](../../../docs/releases.md#0100-function-files).

## Exact-tag qualification

The tagged source passed all 78 stages with 3,851 reported assertion executions and 446 frozen artifact files, on the workflow's macOS 15.8.2 arm64 runner with .NET SDK 10.0.403, Python 3.13.17 and Node v24.21.0. This includes the three-language file and gateway suites against source-built and packed SDKs. [qualification.json](qualification.json) retains the environment, source identity and stage outcomes; three permitted consumer lockfile refreshes are recorded there.

The frozen artifact inventory SHA-256 is `de85142598be1b920fafff922d090ba66c67883067a35c6b795b184fc508d65b`. [release-manifest.json](release-manifest.json) identifies all original package, symbol and author SDK hashes. No rebuilt artifact was substituted after qualification.

## Publication verification

All 16 public GitHub package, symbol and SDK downloads match the qualified bytes exactly; see the [GitHub verification](github-publication-check.json). All seven public NuGet package payloads match the qualified contents, allowing only NuGet's added repository signature; see the [NuGet verification](publication-check.json). The protected workflow also published the matching symbols.

The complete [SonarQube report](sonar/summary.md) for the tagged commit records a successful quality gate with zero open issues, zero unreviewed hotspots and 83.7 % coverage on new code. The [audit](audit.md) records the review rounds and their fixes. This release introduces no new sandboxing, capacity or performance guarantee.
