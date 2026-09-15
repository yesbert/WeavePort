# Public release 0.3.0

Optional local MCP tools from annotated tag `v0.3.0`, commit `9081441f183d6bbdae0812f4ad3a738ec3712cae`. Native remains the default. All four core packages and exact compatibility declarations use 0.3.0; host API and native wire protocol remain 1.

- [GitHub release](https://github.com/yesbert/WeavePort/releases/tag/v0.3.0)
- [Release workflow](https://github.com/yesbert/WeavePort/actions/runs/34946992867)
- [Original tested packages and symbols](https://github.com/yesbert/WeavePort/releases/download/v0.3.0/weaveport-0.3.0-packages.zip)
- [Qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.3.0/weaveport-0.3.0-evidence.zip)
- [Raw benchmark runs, including review iterations](https://github.com/yesbert/WeavePort/releases/download/v0.3.0/weaveport-0.3.0-benchmarks.zip)
- [Release hashes](release-manifest.json) and [qualification summary](qualification.json)

## Verification

The exact tag passed 1,088 assertions and froze 139 artifacts, using .NET SDK 10.0.401 and Node 24.21.0 LTS. The packed MCP example and official TypeScript SDK 2.0.0 interoperability checks exercise both supported revisions. Required Windows, Linux, macOS, documentation and CodeQL checks passed. [Sonar](https://github.com/yesbert/WeavePort/actions/runs/34946737427) reports zero open issues and zero security hotspots, 84.3% new-code coverage and no duplication.

A release review reproduced and fixed an input-required result incorrectly accepted when content was also supplied. The focused MCP suite now has 107 assertions. Subsequent maintainability changes separate receive-loop orchestration from notification and response validation. The normal nuget-org environment gate and OIDC Trusted Publishing published all four packages and sibling symbols.

## Measurements of the released Hosting assembly

These local measurements use the actual Hosting and Abstractions DLLs extracted from the original qualified NuGet packages; Hosting's SHA-256 is recorded in [measurements.json](measurements.json) and was checked against the package entry. The C# test harness calls native TypeScript and official MCP SDK2 echo implementations with Node 24.21.0 on macOS arm64. Five alternating repetitions, 30 warmups per size, and 18,000 successful measured calls. Cells below are medians of per-run statistics, including p99, not pooled request percentiles.

| Protocol | Text characters | Mean ms | p99 ms | First call including startup ms |
| --- | ---: | ---: | ---: | ---: |
| Native | 16 | 0.0574 | 0.1444 | 59.11 |
| Native | 65,536 | 0.2976 | 0.6528 | 59.11 |
| Mcp20251125 | 16 | 0.0862 | 0.2371 | 110.75 |
| Mcp20251125 | 65,536 | 0.3535 | 1.3037 | 110.75 |
| Mcp20260728 | 16 | 0.1006 | 0.3105 | 111.41 |
| Mcp20260728 | 65,536 | 0.3273 | 1.2358 | 111.41 |

MCP adds about 29–43 microseconds to the small-call mean in this run. With 64 KiB text the means are about 10–19% above native, with higher tail latency. Startup is roughly 59 ms native versus 111 ms MCP and can be amortized by retained workers. This includes SDK behavior, serialization, caller payload construction and result checks; it is not pure wire cost, maximum throughput or multi-tenant capacity evidence.

The review iterations are retained in the raw archive, not discarded: large-call means varied between runs. Node and logging dependencies changed since the [original MCP comparison](../../mcp/local-stdio/README.md), so cross-report differences are not a controlled performance regression experiment. The final table is selected because its Hosting DLL matches the released package, not because it is the fastest run. Native caller allocations remain approximately 4.7 KB for small calls versus 6.2–6.8 KB for MCP; allocation is not retained worker memory.

## Scope

Published under MIT: Abstractions, Hosting, Sdk and Sdk.Client. MCP supports explicitly selected 2025-11-25 and 2026-07-28 local stdio tools/list and tools/call. No remote HTTP, resources, prompts, interactive continuations, exported MCP gateway or hostile-code sandbox is claimed. Full release qualification covers macOS arm64; functional Windows/Linux CI is separate from public-package installation and performance qualification. See [the guide](../../../docs/mcp-plugins.md).
