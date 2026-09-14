# Public release 0.2.1

Package branding patch from annotated tag `v0.2.1`, commit `006eb99046d863ca201dbb7adfdb6fd58ed32b3d`. All four core NuGet packages embed the unchanged website logo. Runtime APIs remain unchanged from 0.2.0; exact compatibility declarations use 0.2.1.

- [GitHub release](https://github.com/yesbert/WeavePort/releases/tag/v0.2.1)
- [Release workflow](https://github.com/yesbert/WeavePort/actions/runs/34855701684)
- [Original packages and symbols](https://github.com/yesbert/WeavePort/releases/download/v0.2.1/weaveport-0.2.1-packages.zip)
- [Qualification logs and manifests](https://github.com/yesbert/WeavePort/releases/download/v0.2.1/weaveport-0.2.1-evidence.zip)
- [Release SHA-256 manifest](release-manifest.json)
- [Public NuGet payload and icon verification](publication-check.json)

## Verification

The exact tagged source passed macOS arm64 qualification with 874 assertion executions and 139 frozen files. Required macOS, Windows, Linux, documentation and CodeQL PR checks passed before tagging. Nine release-tool tests cover metadata/provenance checks, including rejection of missing or different icons. Export selected only the original four tested packages and their symbols.

Publication uses the normal nuget-org environment gate and OIDC Trusted Publishing. Public NuGet downloads are compared entry by entry with the original qualified ZIP payloads, excluding NuGet's added repository signature. Each embedded logo matches website/assets/logo.png byte for byte. All four NuGet gallery pages reference their version-specific icon endpoint, which serves the identical PNG. Hashes and gallery URLs are recorded in the verification report.

The [Sonar report for the release commit](https://github.com/yesbert/WeavePort/actions/runs/34855695158) is complete: quality gate OK, zero open issues and zero security hotspots.

## Scope

Published packages: WeavePort.Abstractions, WeavePort.Hosting, WeavePort.Sdk and WeavePort.Sdk.Client under MIT. Host API level and wire protocol remain 1; Python/TypeScript SDKs remain 0.1.0. Full release qualification covers macOS arm64; Windows/Linux public-package installation and capacity/performance qualification remain separate. See [platform limits](../../../docs/platform-qualification.md).
