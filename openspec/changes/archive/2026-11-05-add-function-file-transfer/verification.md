# Function file transfer verification

## Qualified source — 2026-11-05

Commit `eccd6c3b6ec39527145c87d4f25167ab2a4efef1` passed the normal committed-candidate workflow, `./scripts/verify.sh`. The run used a fresh detached checkout and isolated dependency caches. Local retained evidence is `artifacts/candidates/20261105-123422-86da82ef/result.json`, with per-stage logs. Result: **passed, 78 stages, 3,827 assertions, 446 frozen files**. Artifact manifest SHA-256: `668f8613b1d834cbcf95d17564229a3289965ce2b5710fa336396f0422cb0510`.

Environment: macOS 27.2 arm64, .NET SDK 10.0.403, .NET 10.0.14 runtime, Python 3.15.2, Node v26.12.1 and npm 11.20.0. Three permitted consumer lockfile refreshes are recorded in result.json: `examples/mcp`, and the Decision Room host and plugin. Docker was not used.

Commit `eccd6c3` adds four stages to the workflow: `concurrent-sdk-{source,packed}-files` and `concurrent-sdk-{source,packed}-files-gateway`. They run the three-language file and gateway suites against the source-built and the packed SDK fixtures, including the installed Python wheel and npm package. All four passed.

The preceding commit `83aa2d5` passed the workflow without those stages: 74 stages, 3,733 assertions, evidence `artifacts/candidates/20261105-122915-07f67faf`. It is superseded because it did not exercise function files with packed SDKs.

After qualification, only the evidence and task completion, the specification synchronization, archiving and generated documentation changed. The implementation bytes are those of the qualified commit.

## Limits

- Native macOS only; no new Windows or Linux execution claim.
- The gateway was measured over loopback only; no real-network measurement was made.
- No packages were published and no versions were changed. Release as 0.10.0 is a separate decision.

## Local measurements — 2026-11-05

Recorded, not asserted; no performance target is claimed.

Command: `dotnet run --project tests/WeavePort.ConcurrentSdkTests -c Release -- files-measure "$PWD"`. Source: branch `claude/function-file-transfer-proposal` at the commit that adds this file. Environment: macOS 27.2.0 arm64, 10 cores, .NET 10.0.14, Python 3.15, Node v26.12.1. Local native host over stdio, no scheduler, no gateway.

Workload: a 9,437,546-byte ZIP of three stored 3 MiB entries, read through `PluginInputReader.FromStream`.

- **copy**: the plugin reads the whole input and writes it to one output in an in-memory destination.
- **zip-summary**: the plugin lists the entries from the central directory at the end. C# uses `ZipArchive` with synchronous reads; Python uses `zipfile` through `as_file()` on a worker thread; TypeScript uses a minimal parser over `read(position, length)`.

Median of five runs after one warm-up call:

| Language | Block | copy ms | MiB/s | Host reads | Output blocks | ms per round trip | zip-summary ms | zip-summary host reads | Worker MiB initial → peak |
|---|---|---|---|---|---|---|---|---|---|
| C# | 64 KiB | 37.6 | 239.6 | 145 | 145 | 0.13 | 0.3 | 2 | 58.4 → 66.7 |
| C# | 256 KiB | 25.8 | 348.8 | 37 | 37 | 0.349 | 0.6 | 2 | 72.5 → 82.6 |
| Python | 64 KiB | 62.2 | 144.7 | 145 | 145 | 0.215 | 0.5 | 1 | 26.5 → 27.7 |
| Python | 256 KiB | 45.6 | 197.2 | 37 | 37 | 0.617 | 0.5 | 1 | 29.5 → 29.6 |
| TypeScript | 64 KiB | 39.9 | 225.8 | 145 | 145 | 0.137 | 0.3 | 1 | 75.2 → 85.9 |
| TypeScript | 256 KiB | 30.5 | 295.4 | 37 | 37 | 0.412 | 0.2 | 1 | 77.5 → 105.2 |

These figures are after the review fixes, which encode each block once. The first run, before those fixes, measured C# at 208 ms, Python at 86 ms and TypeScript at 64 ms for the 64 KiB copy, with the C# worker growing from 102.5 to 158.3 MiB.

"ms per round trip" is the copy time divided by host reads plus output blocks, so it includes the plugin's own work and base64 handling.

## Gateway over loopback — 2026-11-05

Command: `dotnet run --project tests/WeavePort.ConcurrentSdkTests -c Release -- files-gateway "$PWD"`. Same machine and workload; C# worker. The gateway runs in-process on Kestrel HTTP/2 over loopback; the caller's reader and in-memory destination run in the client. Median of five copies after one warm-up:

| Block | copy ms | MiB/s | Round trips (reads + writes) | ms per round trip |
|---|---|---|---|---|
| 64 KiB | 80.1 | 112.4 | 290 | 0.276 |
| 256 KiB | 49.6 | 181.4 | 74 | 0.671 |

Compared with the local path (38 / 26 ms), the gateway adds about 24–42 ms per 9 MB copy over loopback, and 256 KiB blocks save about a third of the time. Before the review fixes the same run measured 241 / 258 ms. A real network adds its round-trip time to each of the 290 or 74 relayed requests. That is not measured here, and it is where 256 KiB blocks, or later read-ahead, would matter.

## Observations

- **Read-ahead.** Locally and through a loopback gateway, a round trip costs under a millisecond per 64 KiB block. Nothing here argues for read-ahead. A measurement over a real network is the open input for that decision.
- **Block cache.** Listing the ZIP needs one or two host reads in every language, although the archive readers touch the end-of-directory record and the directory separately.
- **Worker memory.** Python stays flat. The C# worker grows by 8–10 MiB and the TypeScript worker by 11–28 MiB during repeated 9 MB copies, consistent with managed-heap growth from per-block buffers. No input is held whole.
- **Single encoding.** The review found that the C# SDK and the host each serialized every block twice. After writing payloads straight into the frame, and writing host input blocks from the rented buffer, the C# copy dropped from 208 to 38 ms. Python and TypeScript gained from the host side too.

## Review fixes — 2026-11-05

The high-effort code review of PR #55 found eight issues, all fixed:

- A host timeout during a relayed gateway read reached the caller as `cancelled`. Cancelling a pending gRPC read aborted the call. Relay I/O is now bound to the session's lifetime; the server sends the call's own failure as the terminal reply and then ends the session. The client reads that reply even if its last acknowledgement fails, and never reuses a session after a failed file call.
- Python name and media-type checks accepted a trailing newline. They now use `fullmatch`.
- A scheduler-enabled host threw `NotSupportedException` for stream operations with files instead of returning `files-unsupported`.
- Repeated reads at the end of an input were never charged against the read budget.
- An unclosed Python synchronous output could try to complete itself from garbage collection.
- Each block was encoded twice in the C# SDK and on the host.
- MCP bindings, and bindings whose worker declined files, started a worker just to refuse.
- Reply validation and the positional fill loop were duplicated.

New checks cover the gateway timeout (stable over five consecutive runs), scheduled stream refusal, empty-read charging, refusal without a worker start, and Python name validation.

## Functional evidence so far

- **Host** (`tests/WeavePort.Hosting.Tests`, 27 file exchange and 36 contract assertions) covers:
  - descriptors, positional reads and refusals;
  - the read budget and both output limits;
  - incomplete outputs, plugin failure, partial commit failure and cancellation;
  - built-in file-handle and directory readers and writers;
  - `LocalPluginClient` and the scheduler path.
- **SDKs** (`tests/WeavePort.ConcurrentSdkTests files`, all three languages) cover:
  - the ZIP directory at the end of a 9 MB input and a byte-exact 9 MB output;
  - an output left open, local refusals, and a host without file descriptors;
  - late reads, cache hits, and Python's refusal of synchronous use on the event loop;
  - a `ResultScope` destination, including a failed call that leaves no result.
- **Gateway** (`tests/WeavePort.ConcurrentSdkTests files-gateway`) covers:
  - all three languages listing the ZIP directory, a byte-exact 9 MB output into the caller's destination, `output-incomplete`, and session reuse;
  - refusals before dispatch for a registration without file limits, outputs above the maximum, and an explicit read budget above the maximum;
  - the registration's budget enforced during a call without an explicit one;
  - a failed commit at the caller, aborted with `CommitFailed` and reported as `output-commit-failed`;
  - caller cancellation, which aborts the caller's outputs and leaves the binding usable after cleanup.
- **Regressions run green:** full host suite, scheduler suite, C# SDK wire checks, cleanup checks, three-language stream qualification, local and remote collection up to 200 MiB, the Python and TypeScript unit tests, and after the gateway change the gateway suite and the local and remote collection suite again.
