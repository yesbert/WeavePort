# Product status

Public NuGet version: **0.1.0**, under MIT, with four core packages: Abstractions, Hosting, Sdk and Sdk.Client. Windows, Linux and macOS are supported targets for trusted stdio execution. Current release validation covers macOS arm64; Windows and Linux release validation is pending. See [platform support and validation](platform-qualification.md). This pre-1.0 API remains subject to evolution. Python/TypeScript registry publication and a new offline distribution are outside this release. The historical internal distribution remains **0.1.0-internal.2**.

Implemented and checked: immutable installed-artifact selection, exact compatibility/API gates, shared coordinator template, guarded native recovery, optional safe diagnostics and standalone offline installation. The [candidate evidence](../reports/release/0.1.0-internal.2/candidate/report.md) records 398 assertion executions; the [distribution evidence](../reports/release/0.1.0-internal.2/distribution/report.md) records 144 application assertions and six offline template builds. These records identify their exact source and artifacts.

Current measurement methodology and results are in [benchmarking](benchmarking.md). The full historical comparison collection is described in [historical evidence](history.md), not a competing current status summary.

## Runtime optimization and soak verification

The repository now includes allocation reductions for SDK size checks and gateway serialization, incremental frame scanning, safe admission diagnostics and a supervised k6 soak runner. These source changes do not replace or republish the frozen **0.1.0-internal.2** delivery. [Optimization evidence](../reports/optimization/runtime-and-soak/README.md) records measured large-result caller allocation reductions of about 40% locally and 20% through the gateway; small-operation latency varies.

The [four-hour completion report](../reports/optimization/runtime-and-soak/four-hour-completion.md) records 7,852,212 operations across 24 tenants, zero unexpected errors, all thresholds passing and verified process/workspace cleanup. Actual k6 duration was 14,400.81021 seconds; global p99 was 173 ms and peak summed RSS including k6 was 1,916 MiB. Regression evidence includes 311 hosting assertions in each of the project-reference and packed consumers, 382 packed multilingual SDK checks and 10 supervisor/report tests. The optimization and cleanup/admission changes are completed and archived; their monitoring loop is paused after success.

This qualifies the recorded native macOS arm64 profile. Individual latency reached 4,845 ms, and k6 memory grew separately from the gateway; longer-duration load-generator behavior remains a measurement consideration. Library startup admission remains eight slots by default; the 24-tenant soak explicitly configures 24. Security boundaries and failure acceptance were preserved. See [soak operation](soak-testing.md) for reproduction and limits.

## Open work

The remaining platform change is [qualify-native-capacity-and-platform-comparison](../openspec/changes/qualify-native-capacity-and-platform-comparison/tasks.md): Windows capacity/performance qualification remains outstanding. Native source adapter checks now pass in [Windows/Linux CI](continuous-integration.md), including 38 Windows stdio assertions and 38/39 Linux stdio/socket assertions. Those source checks do not replace current public-package installation qualification. See [platform requirements](platform-qualification.md).

HiveWeaver, TreeWeaver and NextPA integration remains separate application work. Signing/notarization, stronger sandboxing and automatic deployment/migration are separate release decisions, not hidden tasks needed to run the current internal examples.

The [repository cleanup qualification](../reports/verification/current/README.md) adds a maintained-link check (399 assertion executions), moved adapter checks and a packaging regression. It does not replace the exact current release evidence above.
