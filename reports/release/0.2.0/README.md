# Public release 0.2.0

Published on 2026-09-14 from annotated tag `v0.2.0`, commit `1f4d25ef23dc4812a2e3bec3131af5fb54e5072b`. MIT licensing and the four-package allowlist are unchanged.

- [GitHub release and migration notes](https://github.com/yesbert/WeavePort/releases/tag/v0.2.0)
- [Successful release workflow](https://github.com/yesbert/WeavePort/actions/runs/34851207576)
- [Sonar quality gate for the released commit](https://github.com/yesbert/WeavePort/actions/runs/34851198214)

## Qualification and artifacts

The release workflow qualified the exact tagged commit on macOS arm64: 874 assertion executions, 139 frozen files, SDK 10.0.401, Python 3.13.15 and Node v22.23.2. Source/packed host, gateway, API/dependency compatibility, all three applications, multilingual SDKs, recovery, installation refusal and negative package-drift checks passed. Required macOS, Linux, Windows, documentation and CodeQL PR checks passed before tagging.

The exporter selected only original tested NuGet packages and symbols, checking hashes, version, MIT/readme metadata and exact source provenance. The normal nuget-org environment gate approved publication through GitHub OIDC/NuGet Trusted Publishing; all eight uploads succeeded before the GitHub release was announced.

Durable evidence, independent of Actions retention:

- [Original packages and symbols](https://github.com/yesbert/WeavePort/releases/download/v0.2.0/weaveport-0.2.0-packages.zip)
- [Complete qualification logs and manifests](https://github.com/yesbert/WeavePort/releases/download/v0.2.0/weaveport-0.2.0-evidence.zip)
- [Release SHA-256 manifest](release-manifest.json)
- [Public NuGet payload verification](publication-check.json)

All four 0.2.0 packages were subsequently downloaded from the public NuGet V3 service. Every ZIP entry matches the tested package payload, excluding NuGet's added repository signature. The verification records original and signed NuGet package hashes separately.

## Scope

Published packages: WeavePort.Abstractions, WeavePort.Hosting, WeavePort.Sdk and WeavePort.Sdk.Client. Host compatibility level/wire protocol remain 1; Python/TypeScript SDKs remain 0.1.0. Experimental package publication and a replacement historical offline distribution are outside this release.

Trusted stdio targets Windows, Linux and macOS. Full release qualification covers macOS arm64; Windows/Linux public-package installation and Windows capacity/performance qualification remain separate. This is not hostile-plugin sandbox qualification or a new performance measurement. See [platform limits](../../../docs/platform-qualification.md).
