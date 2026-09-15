# Optional local MCP integration evidence

Source candidate measured on macOS on 2026-09-15, .NET SDK 10.0.401 / net10.0, Node v24.18.0. These are source-built candidate measurements, not qualification of the published 0.2.1 binaries or another operating system.

## Scope

The native before/after suite measures the existing three-language SDK path through local and gateway topologies. The MCP comparison separately measures complete C# PluginHost calls to the native TypeScript SDK and official MCP SDK 2.0.0, using the same echo function and explicitly selected revisions. Runtime dependencies of the distributed Hosting package are unchanged.

## Integrated MCP comparison

`measurements.json` retains five alternating repetitions per protocol and two payload sizes. Each process performs 30 warmups per size, then 1,000 small and 200 large measured calls: 18,000 successful measured calls. Every complete returned text is validated. Raw per-call samples remain under the ignored artifacts/mcp-integration-baseline directory. The compact file records actual host/runtime identity and per-run statistics.

Values below are medians of the five per-run statistics. A median of per-run p99 values is not a pooled percentile. Calls include caller JSON construction, complete response and validation. First-call latency starts after host construction/binding and includes worker startup. Managed allocation includes all coordinator activity during the interval, not worker memory.

| Protocol | Text characters | Mean ms | p99 ms | First call ms | Caller allocated B/call |
| --- | ---: | ---: | ---: | ---: | ---: |
| Native | 16 | 0.0575 | 0.1420 | 50.83 | 4,663 |
| Mcp20251125 | 16 | 0.0842 | 0.2908 | 101.50 | 6,224 |
| Mcp20260728 | 16 | 0.1053 | 0.3460 | 101.46 | 6,760 |
| Native | 65,536 | 0.3557 | 0.7626 | 50.83 | 267,713 |
| Mcp20251125 | 65,536 | 0.3619 | 1.2453 | 101.50 | 269,153 |
| Mcp20260728 | 65,536 | 0.3669 | 1.1279 | 101.46 | 269,672 |

This fixture shows additional small-call/startup cost and higher tails with MCP. The large-payload means do not establish that either protocol is universally faster. SDK implementations, validation and envelopes differ; this is not pure wire cost. Sequential calls are not maximum throughput, multi-tenant load interference or memory-containment evidence. The earlier Node-only spike is not used as evidence of C# integration performance.

## Security and compatibility boundary

Deterministic host regressions cover malformed frames/UTF-8, size and depth, duplicate/foreign IDs, conflicting results, unexpected server requests, notification flooding, unsupported interactions, stderr draining, native callback-grant rejection, state retention, shared quotas, cancellation, crash recovery, pristine/idle/restart and cleanup. The official TypeScript SDK fixture verifies real discovery and tool calls under both selected revisions. These tests do not claim containment of malicious same-user code, hard resource ceilings, escaped descendants or network restrictions.

See [fixture reproduction](../../../tests/mcp/README.md) and [consumer guide](../../../docs/mcp-plugins.md). Native regressions, official interoperability and packed-consumer verification have different scopes; none is an external penetration-test certification.

## Dependency review

`dependencies.json` records the exact development-only npm closure, registry artifact integrity and hashes of installed license notices. Direct dependencies are the official MCP server and its schema library; the core package is transitive. These npm artifacts are not included in WeavePort NuGet packages.

MCP 2.0.0 package metadata declares MIT. The exact LICENSE files instead describe a transition: new and consented contributions use Apache-2.0, older unconsented contributions retain MIT, and documentation excluding specifications uses CC-BY-4.0. Both code-license texts are included in the artifacts. zod's exact artifact carries MIT. Preserve applicable copyright/license notices and resolve contribution-level attribution before bundling these dependencies; this inventory is not blanket redistribution clearance. No external MCP SDK source was copied into the host implementation.

## Native before/after and follow-up controls

The standard source-built suite completed 24 warm timing cells and 382 SDK checks both before and after the change. Existing host transport, hostile-envelope, lifecycle and authority checks passed before runtime edits. The new host suite adds 103 MCP protocol/lifecycle/authority assertions. `native-comparison.json` retains all original means, deviations and allocation statistics, including slower observations rather than discarding them.

The first short after-run showed notably higher C# callback means and strong within-run settling. A separate isolated in-process callback control used ten warmups and ten measured iterations, reversed order (after then before), copied runner/gateway directories and the original Hosting DLL retained from the baseline. All six cases passed in each control run. C#/Python changes ranged from -3.2% to +2.0%; TypeScript was not slower. `callback-control.json` retains the full iteration values and DLL hashes. This control does not use the same toolchain as the original suite; compare its paired values only. It does not demonstrate a universal optimization or exact performance equality.

Raw failed attempts remain under artifacts/mcp-integration-baseline: the initial default benchmark hit source-package hash drift, and early experimental control runners had inherited-job/dependency setup failures. They are excluded from completed-run counts. The later paired-3 control completed successfully without these setup failures. No Docker service was stopped or reconfigured.

A matching follow-up for C# list-8m completed both topologies with ten warmups and ten measured iterations. The paired changes were +0.6% through the gateway and -3.4% locally; `payload-control.json` retains the values. The originally slower callback/large-payload observations therefore did not reproduce as a consistent native regression in these focused controls. Short local measurements still cannot promise exact performance equality in other workloads or deployments.
