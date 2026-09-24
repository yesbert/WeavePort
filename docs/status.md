# Product status

## Release 0.8.0


Release version: **0.8.0**, under MIT, with four core packages and optional Composition, Gateway server and Gateway client packages. Windows, Linux and macOS are supported targets for trusted stdio execution. Current release validation covers macOS arm64; Windows and Linux release validation is pending. See [platform support and validation](platform-qualification.md). This pre-1.0 API remains subject to evolution. Python/TypeScript registry publication and a new offline distribution are outside this release. The historical internal distribution remains **0.1.0-internal.2**.

The previous [0.7.0 release evidence](../reports/release/0.7.0/README.md) records 3,533 reported qualification assertions, the clean static audit and verified downloads for all seven NuGet packages and 16 GitHub artifacts.

The previous [0.5.0 release evidence](../reports/release/0.5.0/README.md) records all seven published packages, 3,496 qualification assertions, artifact provenance and public download verification.

The [0.3.0 release evidence](../reports/release/0.3.0/README.md) records 1,088 assertion executions, exact tested package/symbol provenance, successful Trusted Publishing and measurements of the released Hosting assembly. Earlier [0.2.1 evidence](../reports/release/0.2.1/README.md) remains available as historical release data.

Implemented and checked: immutable installed-artifact selection, exact compatibility/API gates, shared coordinator template, guarded native recovery, optional safe diagnostics and standalone offline installation. The [candidate evidence](../reports/release/0.1.0-internal.2/candidate/report.md) records 398 assertion executions; the [distribution evidence](../reports/release/0.1.0-internal.2/distribution/report.md) records 144 application assertions and six offline template builds. These records identify their exact source and artifacts.

Current measurement methodology and results are in [benchmarking](benchmarking.md). The full historical comparison collection is described in [historical evidence](history.md), not a competing current status summary.

## Release qualification and availability

Release 0.8.0 includes structured failure details, explicit cancellation attribution, primary execution/cleanup cause preservation, safe diagnostic delivery, generated shared protocol definitions and the complete readability audit. Python/TypeScript author SDKs are 0.4.0. See [migration notes](releases.md#080-failure-contracts-and-code-quality), [failure codes](failure-codes.md), [runtime diagnostics](runtime-diagnostics.md) and [source organization](source-organization.md).

Publication is being prepared. The [0.8.0 release record](../reports/release/0.8.0/README.md) tracks exact-version qualification and public download verification; until publication completes, 0.7.0 remains the latest public delivery. Current API baselines and examples describe 0.8.0. Historical reports retain their original versions and qualification limits.

## Runtime optimization and soak verification

The repository now includes allocation reductions for SDK size checks and gateway serialization, incremental frame scanning, safe admission diagnostics and a supervised k6 soak runner. These source changes do not replace or republish the frozen **0.1.0-internal.2** delivery. [Optimization evidence](../reports/optimization/runtime-and-soak/README.md) records measured large-result caller allocation reductions of about 40% locally and 20% through the gateway; small-operation latency varies.

The [four-hour completion report](../reports/optimization/runtime-and-soak/four-hour-completion.md) records 7,852,212 operations across 24 tenants, zero unexpected errors, all thresholds passing and verified process/workspace cleanup. Actual k6 duration was 14,400.81021 seconds; global p99 was 173 ms and peak summed RSS including k6 was 1,916 MiB. Regression evidence includes 311 hosting assertions in each of the project-reference and packed consumers, 382 packed multilingual SDK checks and 10 supervisor/report tests. The optimization and cleanup/admission changes are completed and archived; their monitoring loop is paused after success.

This qualifies the recorded native macOS arm64 profile. Individual latency reached 4,845 ms, and k6 memory grew separately from the gateway; longer-duration load-generator behavior remains a measurement consideration. Library startup admission remains eight slots by default; the 24-tenant soak explicitly configures 24. Security boundaries and failure acceptance were preserved. See [soak operation](soak-testing.md) for reproduction and limits.

## Open work

The remaining platform change is [qualify-native-capacity-and-platform-comparison](../openspec/changes/qualify-native-capacity-and-platform-comparison/tasks.md): Windows capacity/performance qualification remains outstanding. Native source adapter checks now pass in [Windows/Linux CI](continuous-integration.md), including 38 Windows stdio assertions and 38/39 Linux stdio/socket assertions. Those source checks do not replace current public-package installation qualification. See [platform requirements](platform-qualification.md).

HiveWeaver, TreeWeaver and NextPA integration remains separate application work. Signing/notarization, stronger sandboxing and automatic deployment/migration are separate release decisions, not hidden tasks needed to run the current internal examples.

The [repository cleanup qualification](../reports/verification/current/README.md) adds a maintained-link check (399 assertion executions), moved adapter checks and a packaging regression. It does not replace the exact current release evidence above.

## MCP support

The 0.3.0 release adds [optional local MCP tools](mcp-plugins.md) alongside the native protocol, with explicit 2025-11-25/2026-07-28 selection. Included in subsequent releases, including 0.8.0. The guide and [measurement report](../reports/mcp/local-stdio/README.md) identify the tested subset and platform limits.

Version 0.3.1 adds public `McpMethods.ListTools` and `McpMethods.CallTool` constants used by the consumer examples. The MCP wire protocol and supported subset remain unchanged.

## Composition and gateway packages

Version 0.5.0 includes the integrated runtime with optional Composition, Gateway server and Gateway client packages. This release adds authenticated local/remote composition, separate client-only deployment, direct TLS tests and package API/dependency gates. Publication and platform claims depend on retained qualification evidence; see [release status](releases.md) and [gateway limits](gateway.md).

## Approved session reuse and fair scheduling

Version 0.4.0 adds a memory-led fair scheduler and opt-in `WorkerReusePolicy.ApprovedSessions`. Customer-bound execution remains the default. The host requires the updated SDK cleanup handshake before sharing compatible reviewed deployments; C#, Python and TypeScript expose registered session resources. See [operator/author guidance](reusable-plugins.md), [candidate verification](../reports/verification/approved-session-reuse-20260920/README.md) and [public release evidence](../reports/release/0.4.0/README.md). Python/TypeScript 0.2.0 artifacts accompany that historical GitHub release; current 0.8.0 uses author SDK 0.4.0; separate registry publication is not claimed.

## Unified host and shared execution

The current source exposes one `PluginHost` with queued or immediate admission, explicit reconstructible eviction, operation-wide stream/source residency, verified installation approvals and opt-in resident Shared execution. Shared invocation authority, cancellation retention and bounded recovery are separate from sequential `ApprovedSessions` cleanup. New functionality must be qualified against the candidate package family before release claims; historical reports above remain tied to their recorded versions and machines. See [shared execution](shared-execution.md) and [installed clients](installed-plugin-clients.md).

## Portable installations and consumer diagnostics

Current source adds schema-2 runtime requirements derived from .NET, Python and Node declarations, bounded runtime checks at resolution and worker startup, and a public .NET sealing API using embedded compatibility metadata. Legacy manifests retain strict runtime hashes. A managed-only bundle was sealed on macOS and invoked unchanged from an external plugin-root mount in a Linux container; this does not replace full Linux release qualification. See [portable installation guidance](portable-installations.md) and [verification evidence](../reports/verification/portable-runtime-20260923/summary.md). The [0.7.0 release record](../reports/release/0.7.0/README.md) retains successful publication and download verification.

Version 0.7.0 also adds complete `ListAll` diagnostics, explicit assembly-derived author versions, structured startup mismatch details and immutable per-call local/gateway timing. See the [release audit](../reports/release/0.7.0/audit.md) and [client guide](plugin-sdk.md#per-call-timing).
