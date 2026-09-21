# Streaming throughput source comparison

Measured 2026-09-21 on .NET 10.0.12/macOS 27.0.0. This is a bounded source diagnostic
of the WPF-01 request to preserve existing streaming throughput cells. The baseline
is commit `45ef50a` (0.5.0); the initial candidate is `a1146a5`. Final measurements
include the Python batching optimization and immediate-prefetch callback fixes
made during this review. Assembly and author runtime hashes are in
[provenance.json](provenance.json). The last C# early-close guard was added after
these timing runs and passed its functional regression; it is not represented
by the measured C# worker hash.

## Method and limits

The [source runner](../../../../benchmarks/WeavePort.Streaming/README.md) reuses the
existing SDK benchmark's workload and full validation: eight or 1,024 JSON rows,
each containing 8,192 text characters plus identity and row ID. Three languages,
local/gateway clients, three warm calls, then six iterations of at least 250 ms
per cell. Baseline and initial candidate ran in ABBA order; the final optimized
candidate ran twice afterward. All requests completed with valid complete results.

Unlike the delivered BenchmarkDotNet harness, the loopback HTTP/2 gateway runs in
the coordinator process. Startup is excluded. These measurements therefore test
matched source workloads, not exact delivered-release topology or universal
performance. Short cells exhibit visible run-to-run variation, and concurrent
machine activity was not suppressed. No Docker services were changed. A targeted
Python run that overlapped functional collection was excluded from the retained
comparison. Public measurement copies redact only the machine-local source path;
raw measurement-file hashes are retained.

## Result

Values below average the median iteration latency from each of two runs; lower
is better. They are not individual-request p95/p99 values. The source diagnostic
**does not establish zero regression in every cell**: the 64 KiB cells include
small average slowdowns, and TypeScript local 8 MiB is approximately 3% slower.
Some of these changes are within the observed variation between baseline runs;
these bounded repeats cannot establish a production SLO or a universal speedup.

| Language | Topology | Workload | Baseline ms | Final ms | Change |
| --- | --- | --- | ---: | ---: | ---: |
| csharp | local | list-64k | 0.402 | 0.428 | +6.6% |
| csharp | local | list-8m | 33.821 | 32.358 | -4.3% |
| csharp | gateway | list-64k | 0.609 | 0.582 | -4.3% |
| csharp | gateway | list-8m | 39.282 | 30.522 | -22.3% |
| python | local | list-64k | 0.627 | 0.677 | +8.1% |
| python | local | list-8m | 56.007 | 53.743 | -4.0% |
| python | gateway | list-64k | 0.781 | 0.855 | +9.4% |
| python | gateway | list-8m | 64.101 | 54.618 | -14.8% |
| typescript | local | list-64k | 0.305 | 0.315 | +3.3% |
| typescript | local | list-8m | 32.302 | 33.258 | +3.0% |
| typescript | gateway | list-64k | 0.533 | 0.547 | +2.6% |
| typescript | gateway | list-8m | 40.926 | 36.913 | -9.8% |

## Regression found and corrected

The initial candidate showed a repeatable Python 8 MiB slowdown of approximately
67–69% locally and 55–58% through the gateway. Per-item task creation and waiting
added scheduler overhead to an already-ready generator. The Python SDK now uses
one retained producer, bounded ready items/bytes, one pending advancement and one
batch-level readiness wait. A subsequent small correction avoids awaiting an
already-completed producer through `gather` and avoids a separate waiter task.

The final Python 8 MiB cells are approximately 4% faster locally and 15% faster
through the gateway than the two-run baseline summary. The 64 KiB Python summary
still averages 8–9% slower, with baseline variation from 0.574 to 0.679 ms locally.
This is explicitly retained as a limitation rather than silently treating the
literal no-regression requirement as proved.

The optimization and audits also exercise immediate `yield item; await callback`
behavior: buffered items must be delivered before a prefetched callback can wait
for consumer-dependent work. Early close after that first item must cancel the
deferred callback without needing a callback reply. C#, Python and TypeScript
wire tests cover these cases; the six local/gateway collection lanes additionally
pass a real-host consumer-controlled callback barrier with no artificial delay.
