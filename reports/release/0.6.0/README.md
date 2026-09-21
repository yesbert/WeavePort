# Public release 0.6.0

All seven NuGet packages at annotated tag `v0.6.0`, commit `aa69112c2e351c0de800808566022773b3813136`, deliver unified host admission, approved resident shared execution, installed plugin clients, live streams and bounded binary source collection. Python and TypeScript author SDKs are 0.3.0, distributed as qualified wheel/tarball assets; this is not PyPI/npm registry publication.

- [Release and original package downloads](https://github.com/yesbert/WeavePort/releases/tag/v0.6.0)
- [Durable qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.6.0/weaveport-0.6.0-evidence.zip)
- [Public NuGet verification](publication-check.json)
- [Protected release workflow](https://github.com/yesbert/WeavePort/actions/runs/35652454003)
- [Original artifact hashes](release-manifest.json) and [exact-tag qualification](qualification.json)
- [Audit and corrections](audit.md), [dependency advisory check](dependency-audit.json) and [final SonarQube report](sonar-summary.md)
- [Streaming comparison and limits](streaming/README.md) and [large-source development measurements](source-development.json)

## Package and implementation scope

The 0.6.0 family comprises `WeavePort.Abstractions`, `WeavePort.Hosting`, `WeavePort.Sdk`, `WeavePort.Sdk.Client`, `WeavePort.Composition`, `WeavePort.Sdk.Gateway.Client` and `WeavePort.Sdk.Gateway`, plus seven sibling symbol packages. Testing remains internal.

One PluginHost admits exclusive and explicitly approved shared work against the same budget. Catalog discovery, sealed launch declarations and one binding approval produce bound clients directly. Concurrent C#, Python and TypeScript workers isolate invocation identity, callbacks, cancellation and cleanup. Streams retain their exclusive worker for the whole operation, deliver available items promptly and use separate exchange/total deadlines. Composition collects large plugin-originated byte sources with quotas, cancellation and cleanup through local or gateway clients.

Use the [shared installation example](../../../examples/shared/README.md), [source/live-stream example](../../../examples/sources/README.md), [installed-client guide](../../../docs/installed-plugin-clients.md) and [migration notes](../../../docs/releases.md). Upgrade the selected .NET family together and regenerate declarations for host API 2. Shared native workers use protocol 2; exclusive workers retain protocol 1. Shared streams, binary sources, Docker and MCP are not supported by the shared mode. Customer-bound execution remains the default; cooperative cleanup is not a process sandbox.

## Verification

The exact release tag passed **3,530 reported assertions** and froze **364 artifacts** in a clean macOS checkout with isolated dependency caches. Qualification includes source and packed consumers, both new examples, three-language shared/callback failure handling, cancellation and recovery, six local/gateway 200 MiB collection lanes, live-stream and paused-consumer ownership, API compatibility and rejection of changed packages. Additional SDK checks are retained in logs even where a suite reports one summary instead of an assertion count.

The final [main CI](https://github.com/yesbert/WeavePort/actions/runs/35651608863), [CodeQL](https://github.com/yesbert/WeavePort/actions/runs/35651608551) and [SonarQube analysis](https://github.com/yesbert/WeavePort/actions/runs/35651608629) passed on the tagged revision. SonarQube reports **zero open issues**, **zero unreviewed hotspots**, **85.6% new-code coverage**, **83.7% overall coverage** and **zero duplication**. Native adapter checks also passed on Linux and Windows; this does not extend the documented platform qualification or containment guarantees.

The audit corrected shutdown lock ordering, bounded callback retention after cancellation and all 30 static-analysis findings without suppressing rules or relaxing gates. All 16 exported package/symbol/SDK files matched the frozen manifest before the existing nuget-org environment was approved. OIDC publication and GitHub release announcement succeeded. All 16 public GitHub downloads match the original bytes. All seven public NuGet downloads were verified after indexing: every original ZIP entry matches, with only NuGet’s repository signature permitted as an additional entry. Original and public file hashes are retained separately.

## Performance evidence and limits

Concurrency benchmarks demonstrate overlapping independent calls; measured delay throughput rises from approximately 6.6 calls/s at degree one to 104 calls/s at degree sixteen on the documented machine. This is not a universal capacity recommendation. Large-source development optimization reduced measured coordinator allocations by approximately 53% locally and 38% through the gateway for the measured 200 MiB workload.

Matched streaming measurements found and corrected a substantial Python regression caused by per-item scheduling. The final measured Python 8 MiB workloads were approximately 4% faster locally and 15% faster through the gateway than the paired baseline summary. Some small workloads remained 3–9% slower, at microsecond-scale absolute differences; the report preserves this limitation and run variation instead of claiming universal zero regression. Raw measurements retain their original source identities. Subsequent shutdown/callback/static-analysis corrections were functionally requalified, not relabeled as new benchmark runs.
