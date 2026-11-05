# Function file transfer verification

Interim record while the change is open. The committed-candidate qualification (task 6.4) and the gateway path (block 4) are not covered yet.

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

## Observations

- **Read-ahead.** Locally, a round trip costs well under a millisecond per 64 KiB block. Quadrupling the block size saves 8–26 % of copy time. Nothing here argues for read-ahead. The gateway path still has to be measured before that decision is closed.
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
- **Regressions run green:** full host suite, scheduler suite, C# SDK wire checks, cleanup checks, three-language stream qualification, local and remote collection up to 200 MiB, and the Python and TypeScript unit tests.
