# Public release 0.7.0

All seven NuGet packages at annotated tag `v0.7.0`, commit `45df60d7ee788f0118d7967a6064dd9129647f74`, deliver portable installed plugins and consumer diagnostics. Python and TypeScript author SDKs remain 0.3.0 and accompany the release as qualified wheel/tarball downloads; this is not PyPI/npm registry publication.

- [Release and original downloads](https://github.com/yesbert/WeavePort/releases/tag/v0.7.0)
- [Durable qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.7.0/weaveport-0.7.0-evidence.zip)
- [Protected release workflow](https://github.com/yesbert/WeavePort/actions/runs/35905590522)
- [Exact-tag qualification](qualification.json) and [original artifact hashes](release-manifest.json)
- [Public NuGet verification](publication-check.json) and [GitHub download verification](github-publication-check.json)
- [Audit and corrections](audit.md), [dependency advisory snapshot](dependency-audit.json) and [final SonarQube report](sonar-summary.md)
- [Development cross-host portability checks and framework comparisons](portable/README.md)

## Delivery and migration

The package family comprises `WeavePort.Abstractions`, `WeavePort.Hosting`, `WeavePort.Sdk`, `WeavePort.Sdk.Client`, `WeavePort.Composition`, `WeavePort.Sdk.Gateway.Client` and `WeavePort.Sdk.Gateway`, plus seven sibling symbol packages. Testing remains internal.

Public .NET sealing uses the embedded exact compatibility policy and creates deterministic schema-2 installations with ecosystem runtime requirements. Complete catalog diagnostics identify refusals per directory. Explicit assembly-derived author versions and expected/advertised startup diagnostics explain mismatches before dispatch. Typed and JSON calls expose immutable per-call host timing locally and through optional gateway fields; legacy clients return unavailable timing.

Upgrade the selected .NET family together and deliberately reseal installations. Resealing changes the identity and never rewrites persisted recovery pins. Schema 1 and optional strict executable hashes retain their checks. The default author version remains `1`; version mismatches now report `version-mismatch` instead of generic `protocol-error`. Host API 2 and native protocol numbers 1/2 are unchanged. See [migration guidance](../../../docs/releases.md), [portable installations](../../../docs/portable-installations.md), [client APIs](../../../docs/plugin-sdk.md) and the [shared example](../../../examples/shared/README.md).

## Verification

Exact-tag qualification passed **3,533 reported assertions across 58 stages** and froze **414 artifacts** in a clean macOS arm64 checkout with isolated caches. It includes source and packed consumers, multilingual startup/version checks, runtime compatibility oracles, cancellation/recovery, metadata concurrency, gateway compatibility and executable examples. Additional checks retained in logs may report one suite summary rather than individual assertion counts.

The tagged revision passed [main CI](https://github.com/yesbert/WeavePort/actions/runs/35904468683), [CodeQL](https://github.com/yesbert/WeavePort/actions/runs/35904468527) and [SonarQube](https://github.com/yesbert/WeavePort/actions/runs/35904468409). SonarQube reports zero open issues, zero unreviewed hotspots, 84.9% new-code coverage, 83.5% overall coverage and zero duplication. All audit findings were corrected without suppressing rules or weakening gates. Native source adapter checks also passed on Linux and Windows.

Before approving the protected nuget-org deployment, all 16 exported package/symbol/SDK files were matched to the exact-tag frozen inventory. Trusted Publishing and the GitHub announcement succeeded. All 16 public GitHub downloads match the original bytes. All seven public NuGet packages preserve every original ZIP entry; only the repository signature is permitted as an additional entry. Original and signed-public hashes are recorded separately.

## Limits

Native plugins are trusted code. Runtime compatibility is not containment or an attestation of runtime dependencies. The unchanged-bundle Linux experiment retains its development commit and does not establish exact-tag Linux package qualification. Full Windows/Linux release qualification, Native AOT, new capacity/performance guarantees and automatic migration are outside this release. Historical evidence remains tied to its original artifacts.
