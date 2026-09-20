# Public release 0.5.0

All seven NuGet packages at annotated tag `v0.5.0`, commit `9ae803b153e154d4f3ab2824db30ac9a8503d8a4`, integrate the shared fair scheduler and approved session reuse with Composition, Gateway.Client and Gateway. Python and TypeScript author SDKs remain 0.2.0 and are distributed as qualified wheel/tarball release assets. This does not claim PyPI/npm registry publication.

- [Release and original package downloads](https://github.com/yesbert/WeavePort/releases/tag/v0.5.0)
- [Release workflow](https://github.com/yesbert/WeavePort/actions/runs/35523943857)
- [Durable qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.5.0/weaveport-0.5.0-evidence.zip)
- [Original artifact hashes](release-manifest.json) and [qualification results](qualification.json)
- [Detailed audit and correction](audit.md), [dependency advisory check](dependency-audit.json) and [final SonarQube report](sonar-summary.md)
- [Public download verification](publication-check.json)

## Package scope

Published at 0.5.0: `WeavePort.Abstractions`, `WeavePort.Hosting`, `WeavePort.Sdk`, `WeavePort.Sdk.Client`, `WeavePort.Composition`, `WeavePort.Sdk.Gateway.Client` and `WeavePort.Sdk.Gateway`. Testing remains internal. Seven sibling symbol packages accompany the release.

## Qualification

The exact tag passed 3,496 assertions and froze 251 artifacts. Optional checks cover 140 assertions, native composition and complete HTTP delivery through 128 MiB with three-way fan-out. The audit reproduced and corrected non-array gateway batch validation, then exercised six negative variants and authenticated recovery. Additional local packed Docker controls passed 344 assertions each for C#, Python and TypeScript (1,032 total) on the audited PR source `d421214633c88663cf369c9521445eb2673dcd0b`; both reuse policies were exercised. The existing SDK suite passed 382 checks.

All 16 exported package/symbol/SDK files matched the frozen qualification manifest before approving the established nuget-org environment. OIDC Trusted Publishing and the release announcement completed successfully; GitHub environment protection rules were not changed. The initial upload stopped at Composition with HTTP 403 because the NuGet policy listed only the four original IDs. After explicit owner approval, the three optional IDs were authorized and the failed jobs resumed on the same workflow with the same artifacts. The already-published Abstractions version was skipped. The owner subsequently approved the scoped `WeavePort.*` pattern for future package additions; repository, workflow and environment bindings remain unchanged.

The final [main CI](https://github.com/yesbert/WeavePort/actions/runs/35523607934), [CodeQL analyses](https://github.com/yesbert/WeavePort/actions/runs/35523607807) and [SonarQube quality gate](https://github.com/yesbert/WeavePort/actions/runs/35523607798) passed. SonarQube reports zero open issues, zero unreviewed hotspots, 84.6% coverage of new code, 79.3% overall coverage and no duplication.

All 16 public GitHub package/symbol/SDK downloads match the original qualified bytes. Standalone SDK licenses match the repository MIT license. All seven public NuGet downloads were checked after indexing: every original ZIP entry matches, with only NuGet's added repository signature permitted. Original and public hashes are retained separately.

## Usage and limits

See the [plugin author and operator guide](../../../docs/reusable-plugins.md), [maintained examples](../../../examples/reuse/README.md), [gateway limits](../../../docs/gateway.md) and [migration notes](../../../docs/releases.md). Update core and optional packages to the same version and regenerate exact installation declarations.

Customer-bound execution remains the default. Approved cleanup is cooperative and does not remove arbitrary hidden state or unregistered background activity. The application owns global admission, credential rotation, private composition storage and crash-orphan recovery. Historical benchmarks keep their original source and machine identities; functional and loopback TLS qualification do not establish new server-capacity or WAN-performance claims.
