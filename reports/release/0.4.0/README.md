# Public release 0.4.0

The four core NuGet packages at annotated tag `v0.4.0`, commit `e319b9cca352569fcc35aef8712a328964a68bf8`, add shared fair scheduling and explicit operator-approved session reuse. Python and TypeScript author SDKs are 0.2.0 and are distributed as qualified wheel/tarball release assets. This does not claim PyPI/npm registry publication.

- [Release and original package downloads](https://github.com/yesbert/WeavePort/releases/tag/v0.4.0)
- [Release workflow](https://github.com/yesbert/WeavePort/actions/runs/35519661080)
- [Durable qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.4.0/weaveport-0.4.0-evidence.zip)
- [Original artifact hashes](release-manifest.json) and [qualification results](qualification.json)
- [Audit corrections](audit.md) and [final SonarQube report](sonar-summary.md)

## Qualification

The exact tag passed 3,345 assertions and froze 175 artifacts. This includes 1,036 source and 1,036 packed multilingual reuse assertions and nine alternating-customer checks through the maintained author examples. All ten exported package/symbol/SDK files matched the frozen qualification manifest before approving the established nuget-org environment. OIDC Trusted Publishing and the release announcement completed successfully; protection rules were not changed.

The final [main CI](https://github.com/yesbert/WeavePort/actions/runs/35519416341), [CodeQL analyses](https://github.com/yesbert/WeavePort/actions/runs/35519416281) and [SonarQube quality gate](https://github.com/yesbert/WeavePort/actions/runs/35519416224) passed. SonarQube reports zero open issues, zero unreviewed hotspots, 82.7% coverage of new code and no duplication. The audit corrected the scheduler completion handoff, qualified reuse consumers and examples, aligned release identities, included SDK license/readme files, and resolved the static-analysis findings.

All ten public GitHub package/symbol/SDK downloads match the original qualified bytes. The standalone SDK licenses also match the repository MIT license. All four public NuGet downloads were checked after indexing: every original ZIP entry matches, with only NuGet’s added repository signature permitted. See [public download verification](publication-check.json).

## Usage and limits

See the [plugin author and operator guide](../../../docs/reusable-plugins.md), [maintained examples](../../../examples/reuse/README.md) and [migration notes](../../../docs/releases.md). Update all four core package versions and exact installation declarations together.

Customer-bound execution remains the default. Approved cleanup is cooperative and does not remove arbitrary hidden state or unregistered background activity. Historical benchmark results retain their original artifacts and machine identities; this release qualification does not establish new maximum server capacity or Windows performance parity.
