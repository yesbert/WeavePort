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
| C# | 64 KiB | 208.3 | 43.2 | 145 | 145 | 0.718 | 0.9 | 2 | 102.5 → 158.3 |
| C# | 256 KiB | 191.9 | 46.9 | 37 | 37 | 2.593 | 2.4 | 2 | 118.8 → 125.8 |
| Python | 64 KiB | 85.6 | 105.2 | 145 | 145 | 0.295 | 0.5 | 1 | 26.9 → 27.1 |
| Python | 256 KiB | 74.9 | 120.2 | 37 | 37 | 1.012 | 0.5 | 1 | 29.3 → 29.3 |
| TypeScript | 64 KiB | 64.4 | 139.8 | 145 | 145 | 0.222 | 0.3 | 1 | 74.6 → 89.1 |
| TypeScript | 256 KiB | 47.6 | 189.1 | 37 | 37 | 0.643 | 0.2 | 1 | 77.9 → 105.5 |

"ms per round trip" is the copy time divided by host reads plus output blocks, so it includes the plugin's own work and base64 handling.

## Gateway over loopback — 2026-11-05

Command: `dotnet run --project tests/WeavePort.ConcurrentSdkTests -c Release -- files-gateway "$PWD"`. Same machine and workload; C# worker. The gateway runs in-process on Kestrel HTTP/2 over loopback; the caller's reader and in-memory destination run in the client. Median of five copies after one warm-up:

| Block | copy ms | MiB/s | Round trips (reads + writes) | ms per round trip |
|---|---|---|---|---|
| 64 KiB | 241.3 | 37.3 | 290 | 0.832 |
| 256 KiB | 257.9 | 34.9 | 74 | 3.485 |

Compared with the local path (208 / 192 ms), the gateway adds about 30–65 ms per 9 MB copy over loopback. Larger blocks did not help on loopback, where the per-request cost is small and payload handling dominates. A real network adds its round-trip time to each of the 290 or 74 relayed requests. That is not measured here, and it is where 256 KiB blocks, or later read-ahead, would matter.

## Observations

- **Read-ahead.** Locally and through a loopback gateway, a round trip costs under a millisecond per 64 KiB block. Nothing here argues for read-ahead. A measurement over a real network is the open input for that decision.
- **Block cache.** Listing the ZIP needs one or two host reads in every language, although the archive readers touch the end-of-directory record and the directory separately.
- **Worker memory.** Python stays flat. The TypeScript and C# workers grow by 14–56 MiB during repeated 9 MB copies. That is consistent with managed-heap growth from per-block buffers and base64 strings. No input is held whole: the worker never reaches 9 MB × repetitions. A bounded-allocation pass over the C# path is a candidate follow-up.
- **C# throughput** is the lowest of the three, at about 2.5× slower than Python per round trip. The C# runtime serializes each block payload twice: once into a `JsonElement` for the callback, then again into the frame. This is the first place to look if the difference matters. At 9 MB the copy still completes in about 0.2 s.

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
