# Runtime optimization — 2026-09-13

Source-built packed candidate compared with unchanged source at `162fade1718a4a4ddfd060fdc28a2ebf74e582f4`, on the same Mac and runtimes. This does not replace the qualified internal.2 release bytes or its retained baseline. [Exact source/package/fixture identities](identity.json) and [distributions](comparison.json) accompany these results.

All 30 cells and 382 SDK correctness checks passed before and after. The final run repeats the initial optimized measurement after a readability-only frame helper extraction. Large-stream caller allocations consistently decreased approximately 40% locally and 20% remotely. Final large-result mean latency decreased 8–17%; small-operation means varied in both directions. Small samples and background activity preclude a universal speedup claim. Cold lifecycle remains dominated by setup/start/disposal; no startup or GC policy was changed.

Accepted changes: count actual serialized UTF-8 bytes without allocating output arrays; directly serialize gateway batches; transfer fresh exclusive serializer arrays into protobuf without a second copy; scan only newly received frame bytes. Existing escaping, UTF-8/depth/size checks, cancellation, authority and worker ownership remain intact. Rejected shortcuts: raw-text lengths (different escaping semantics), pooled protobuf backing arrays (asynchronous ownership risk), relaxed limits, used-worker sharing and deployment GC/reserve tuning without workload evidence.

Complete SDK operations; arithmetic means, not request percentiles. Six requested iterations per cell; retain dispersion and outlier exclusions. Cold includes setup, provenance recording and disposal. Allocations cover the measured caller process only.

| Case | Before ms | After ms | Mean change | Before allocated B/op | After allocated B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| SdkBenchmarks:Language=csharp&Topology=gateway&Case=callback | 0.229 | 0.206 | -10.1% | 4506 | 4425 |
| SdkBenchmarks:Language=csharp&Topology=gateway&Case=list-64k | 0.751 | 0.720 | -4.2% | 335452 | 269547 |
| SdkBenchmarks:Language=csharp&Topology=gateway&Case=list-8m | 58.272 | 51.693 | -11.3% | 42540818 | 34054716 |
| SdkBenchmarks:Language=csharp&Topology=gateway&Case=tiny | 0.148 | 0.143 | -3.7% | 3737 | 3697 |
| SdkBenchmarks:Language=csharp&Topology=local&Case=callback | 0.112 | 0.073 | -35.0% | 7045 | 6964 |
| SdkBenchmarks:Language=csharp&Topology=local&Case=list-64k | 0.306 | 0.269 | -12.2% | 337945 | 206288 |
| SdkBenchmarks:Language=csharp&Topology=local&Case=list-8m | 33.421 | 28.192 | -15.6% | 42556049 | 25651401 |
| SdkBenchmarks:Language=csharp&Topology=local&Case=tiny | 0.044 | 0.042 | -3.7% | 3895 | 3893 |
| SdkBenchmarks:Language=python&Topology=gateway&Case=callback | 0.418 | 0.404 | -3.3% | 4506 | 4426 |
| SdkBenchmarks:Language=python&Topology=gateway&Case=list-64k | 1.180 | 1.032 | -12.5% | 335424 | 269565 |
| SdkBenchmarks:Language=python&Topology=gateway&Case=list-8m | 84.424 | 76.111 | -9.8% | 42464622 | 34032766 |
| SdkBenchmarks:Language=python&Topology=gateway&Case=tiny | 0.235 | 0.204 | -13.2% | 3736 | 3697 |
| SdkBenchmarks:Language=python&Topology=local&Case=callback | 0.183 | 0.183 | +0.1% | 7042 | 6963 |
| SdkBenchmarks:Language=python&Topology=local&Case=list-64k | 0.655 | 0.602 | -8.2% | 337929 | 206258 |
| SdkBenchmarks:Language=python&Topology=local&Case=list-8m | 68.886 | 63.009 | -8.5% | 42501574 | 25649300 |
| SdkBenchmarks:Language=python&Topology=local&Case=tiny | 0.085 | 0.091 | +8.0% | 3912 | 3904 |
| SdkBenchmarks:Language=typescript&Topology=gateway&Case=callback | 0.243 | 0.229 | -6.0% | 4506 | 4426 |
| SdkBenchmarks:Language=typescript&Topology=gateway&Case=list-64k | 0.918 | 0.774 | -15.7% | 335434 | 269553 |
| SdkBenchmarks:Language=typescript&Topology=gateway&Case=list-8m | 65.362 | 54.017 | -17.4% | 42462532 | 34051442 |
| SdkBenchmarks:Language=typescript&Topology=gateway&Case=tiny | 0.224 | 0.161 | -28.0% | 3737 | 3696 |
| SdkBenchmarks:Language=typescript&Topology=local&Case=callback | 0.079 | 0.086 | +9.2% | 7046 | 6966 |
| SdkBenchmarks:Language=typescript&Topology=local&Case=list-64k | 0.417 | 0.307 | -26.3% | 337948 | 206275 |
| SdkBenchmarks:Language=typescript&Topology=local&Case=list-8m | 44.051 | 39.799 | -9.7% | 42503645 | 25652159 |
| SdkBenchmarks:Language=typescript&Topology=local&Case=tiny | 0.044 | 0.045 | +0.2% | 3911 | 3903 |
| SdkColdBenchmarks:Language=csharp&Topology=gateway | 413.632 | 395.302 | -4.4% | — | — |
| SdkColdBenchmarks:Language=csharp&Topology=local | 109.036 | 108.320 | -0.7% | — | — |
| SdkColdBenchmarks:Language=python&Topology=gateway | 422.656 | 378.226 | -10.5% | — | — |
| SdkColdBenchmarks:Language=python&Topology=local | 102.359 | 100.318 | -2.0% | — | — |
| SdkColdBenchmarks:Language=typescript&Topology=gateway | 369.512 | 360.118 | -2.5% | — | — |
| SdkColdBenchmarks:Language=typescript&Topology=local | 79.340 | 78.563 | -1.0% | — | — |

## Reproduction

On each source revision, with no other benchmark/soak running, use a new run name:

```sh
./scripts/build-sdk.sh
./scripts/sdk.sh artifacts/optimization/<name>-correctness verify
./scripts/sdk.sh artifacts/optimization/<name> --filter '*' --artifacts ../../artifacts/optimization/<name>-bdn
```

Preserve each run's package hashes and generated fixture identity records before rebuilding another revision. Compare the complete BDN directories with `python3 -B tools/performance/compare.py <before-bdn> <after-bdn> --output <comparison-directory>`. This candidate workflow must not be substituted for the delivered-package `scripts/benchmark.sh` qualification.

For the prepared four-hour workload and early-stop controls, see [soak operation](../../../docs/soak-testing.md).

## Soak harness qualification

[Retained soak evidence](soak-evidence.json) records successful 3-client/30-second, 24-client/120-second and 24-client/60-second pilots plus a final telemetry check. The two-minute pilot completed 60,702 validated operations, including expected failures; all 24 tenants served work, unexpected errors were zero, p99 was 217.99 ms and peak sampled RSS was 1,618.1 MiB including k6. There were 3,012 separately counted expected provider failures, denials, crashes and cancellations. Cleanup left no owned processes or workspaces.

Deliberately failing validation and 1 MiB RSS controls and an operator STOP control each returned nonzero with the expected reason and clean shutdown. Five supervisor unit checks cover bounded control reads, orphaned fixture-child cleanup, package-byte mismatch rejection, descriptor observation and resource aggregation. The final telemetry pilot additionally exercised actual descriptor sampling and end-of-run artifact checks. Earlier prototype failures are retained only in raw local artifacts; their invalid IDs/transient-quarantine assumptions do not contribute successful measurements.

The four-hour command is prepared; a multi-hour stability result has not yet been produced. Linux/Windows, remote networks, hostile-code containment and production SLOs remain outside this evidence.

## Final verification

Fresh-checkout qualification at `9c850be938ab161b01b39fd3c88b94d14c4cd659` passed **838 assertion executions** and verified **135 frozen artifacts**. [Qualification result](qualification.json) and [artifact hashes](qualification-artifacts.json) identify that source-built candidate; it is not a newly published release. Source and packed host checks, gateway disposal, exact package contracts, application/SDK-version/recovery scenarios, documentation, style and the changed-package negative control all passed.

Separate packed gateway cancellation/idle/parallel/revocation checks and their runtime control passed. The native adapter passed **38 checks** with zero failures using an isolated per-run workspace. The first native attempt found only empty directories retained by a September 11 experiment in the shared fixture root; `verify-local.sh` now selects a new root for its own run, preserving older artifacts. No production cleanup guarantee was changed. The standalone SDK suite additionally passed 382 checks before and after optimization; the five Python supervisor checks and soak controls are described above.
