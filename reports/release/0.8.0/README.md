# Public release 0.8.0

Published on 2026-09-24 from annotated tag `v0.8.0`, commit `9955bc476c25b9d69d6e3b6df2358d5b34937607`. All seven NuGet packages use 0.8.0. Python and TypeScript author SDKs use 0.4.0 and accompany the GitHub release as qualified downloads; this is not PyPI/npm registry publication.

- [Release and original downloads](https://github.com/yesbert/WeavePort/releases/tag/v0.8.0)
- [Durable qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.8.0/weaveport-0.8.0-evidence.zip)
- [Protected release workflow](https://github.com/yesbert/WeavePort/actions/runs/36011332643)
- [Exact-tag qualification](qualification.json) and [original artifact hashes](release-manifest.json)
- [Public NuGet verification](publication-check.json) and [GitHub download verification](github-publication-check.json)
- [Audit and prepublication corrections](audit.md), [final SonarQube analysis](analysis/summary.md) and [analysis completeness/revision](analysis/metadata.json)

## Delivery and migration

The package family comprises `WeavePort.Abstractions`, `WeavePort.Hosting`, `WeavePort.Sdk`, `WeavePort.Sdk.Client`, `WeavePort.Composition`, `WeavePort.Sdk.Gateway.Client` and `WeavePort.Sdk.Gateway`, with seven sibling symbol packages. Testing remains internal.

Structured failure metadata, stable diagnostic codes, explicit cancellation attribution and primary/cleanup cause preservation accompany the complete source-organization and readability improvements. A cancellation/disposal race found before publication was reproduced and corrected, then qualified again. Existing .NET signatures remain available; changed failure interpretation still requires consumer review.

Upgrade selected .NET packages together, use author SDKs 0.4.0, and rebuild/reseal installations offline. Resealing changes manifest identity; retain original deployments for existing recovery pins. Native host API 2 and protocols 1/2 remain unchanged. Gateway fields 10–13 are additive. See the [migration guide](../../../docs/releases.md#080-failure-contracts-and-code-quality), [failure codes](../../../docs/failure-codes.md), [package matrix](../../../docs/package-compatibility.md) and [examples](../../../examples/gateway/README.md).

## Verification

Exact-tag qualification passed **3,599 reported assertion executions across 73 stages**, with **442 frozen artifact files** unchanged. Additional cancellation boundary/resumed-reader assertions are retained in the collection logs. Source and packed consumers, multilingual SDKs, installation/refusal/recovery, API/dependency identities, examples and generated documentation all participate.

The tagged revision passed [main CI](https://github.com/yesbert/WeavePort/actions/runs/36011326117), [CodeQL](https://github.com/yesbert/WeavePort/actions/runs/36011325779) and [SonarQube](https://github.com/yesbert/WeavePort/actions/runs/36011325803). Main Windows/Linux native functional checks passed. SonarQube reports zero open issues, zero unreviewed hotspots, 83.4% overall coverage, 84.5% new-code coverage and zero duplication.

Before the protected publish step, all 16 original package/symbol/SDK exports were matched to the exact-tag inventory. Trusted Publishing and GitHub announcement succeeded. All 16 GitHub downloads match the original bytes. All seven NuGet downloads preserve every original ZIP entry; only the repository signature is permitted as an extra entry. Signed-public and original archive hashes are recorded separately.

## Limits and retained failures

Native plugins remain trusted code. Functional CI does not establish Windows/Linux capacity, Docker containment or long-duration performance guarantees. No Docker services were changed. The first unpublished tag attempt was cancelled after a real gateway cancellation defect was found; its failure and correction remain in the audit. A separate historical intermittent Windows callback failure remains explicitly recorded without a claimed root cause; later checks on the final tagged revision passed. Neither failed run is counted as passing release evidence.
