# Source collection and C# SDK evidence

Measured 2026-09-21 during implementation on .NET 10.0.12, SDK 10.0.401,
macOS 27.0.0. These are warm development-build observations, not the final
qualified release benchmark or portable performance guarantees.

## Functional checks

`tests/WeavePort.ConcurrentSdkTests/check.py` launches the actual C# author
runtime over stdio and checks concurrent tenant identity, fast completion ahead
of a slow invocation, cancellation acknowledgement after handler and cleanup,
reversed callback replies, isolated author failure, immediate stream delivery,
empty heartbeat batches, callbacks after a consumer pause and bounded source
bytes. The `collect` executable mode uses the real local host and loopback
HTTP/2 gateway. Both paths collect and verify all 209,715,200 bytes, enforce a
quota without retaining partial results, cancel collection, recover for another
call and deliver the first live stream item before the slow second item.

Commands:

```sh
dotnet build tests/WeavePort.ConcurrentSdkTests -c Release
python3 tests/WeavePort.ConcurrentSdkTests/check.py
dotnet tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll collect
dotnet run --project examples/sources -c Release
```

The project also supports `UsePackedCore=true` to consume the candidate packages.

## Measured optimization

Each row is one complete warm 200 MiB collection using 256 KiB source blocks,
followed by full integrity-checked delivery. The host and gateway run in the same
consumer process for the remote case. Allocation is cumulative managed allocation
in that process, not peak memory or worker allocation. The generated source uses
constant memory; result storage is a private file.

| Path | Before collection ms | After collection ms | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Local | 983.003 | 583.862 | 1,064,301,704 | 500,690,008 |
| Loopback gateway | 1,336.124 | 751.788 | 1,488,499,976 | 924,398,208 |

The local client previously created a UTF-16 base64 string before decoding each
block. `JsonElement.TryGetBytesFromBase64` removes that full-sized intermediate
copy while retaining decoded-size validation and the transport frame limit.
An unnecessary close after a successful terminal EOF was also removed: SDK EOF
already disposes source/context. The approximately 564 MB allocation reduction
is consistent with removing the base64 UTF-16 copy. Timing also improved in these
runs, but one run per version does not establish a stable speedup distribution.

## Audit corrections

- Serialize C# writes across concurrent invocations and route callback identity
  through one reader; retain per-invocation contexts until handler and cleanup finish.
- Keep bounded completed-ID tombstones for cancellation racing completion;
  genuinely unknown identities and duplicate invocations remain errors.
- Await outstanding stream advancement before iterator disposal. Heartbeats keep
  that same advancement alive and callbacks resume with the next exchange identity.
- Classify source and iterator disposal exceptions as cleanup failures.
- Retain running task ownership through final wire writes on shutdown.
- Give gateway streams independent exchange and total deadlines, and flush live
  batches without waiting to accumulate sixteen items.

Relevant API references: [Task.WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task-1.waitasync?view=net-10.0)
and [PBKDF2 fixture workload](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0).

## Scheduler residency and deadline integration

`dotnet tests/WeavePort.ConcurrentSdkTests/bin/Release/net10.0/WeavePort.ConcurrentSdkTests.dll streams "$PWD"`
passes against actual C#, Python and TypeScript SDK workers. The three language
lanes run independently and check:

- A first stream item delayed 12 seconds succeeds on a binding whose unary
  timeout is five seconds; a 12-second unary call still times out at five seconds.
- An available first item arrives before a deliberately delayed second item.
- Under a two-worker budget, a paused stream keeps its process identity while an
  idle reconstructible neighbor is evicted to admit another tenant.
- Cancelling a paused stream releases its scheduler admission before the consumer
  resumes or disposes the iterator. A new call can run, and late disposal of the
  old iterator leaves the new worker's process identity unchanged.
- Cancelling a queued stream acquisition leaves the scheduler functional.

This provides integration evidence for the two independent stream deadlines and
operation-wide worker residency. The same executable supports packed candidates;
Python and TypeScript paths are selected with `WP_SHARED_PYTHON_SDK` and
`WP_SHARED_NODE_SDK` when qualifying installed author distributions.

## Multilingual source and gateway confirmation

The collection executable subsequently expanded to six lanes: C#, Python and
TypeScript, each through local and loopback gateway clients. All six passed full
200 MiB integrity, quota cleanup, cancellation recovery and live delivery checks.
The live delivery check now uses a callback barrier: the second item cannot
complete until the consumer receives the first item and releases the callback.
This proves incremental delivery without using a sub-second timing threshold.

The development observations are retained in
`reports/release/0.6.0/source-development.json`. Python used approximately 28 MiB
peak worker root RSS for a 200 MiB source (both transports). C# used approximately
68–83 MiB and TypeScript 103 MiB. RSS was sampled every 10 ms, includes runtime
heaps and shared pages, excludes descendants and is not a hard memory limit or
proof about arbitrary author handlers. Host allocations remain cumulative and
are separate from the sampled resident-memory figures. The host and gateway
share a process in these tests, and warmed process memory is retained between
lanes; use the recorded initial and peak values together.

## Remote cancellation while the consumer is paused

An independent audit found that the remote client's rented HTTP/2 session was
bound only to client disposal. An operation's cancellation or total deadline
could therefore remain unobserved while the consumer was paused at a yielded
item. Stream/source operations now register their stop token to abort their
rented transport immediately. The registration is synchronously removed before
returning a session, and a cancelled session is never returned to the reusable
pool.

The six-lane collection suite now runs through the queued host and additionally
checks all three author languages through the real gateway: caller cancellation
of a paused source, caller cancellation of a paused JSON stream, and total-timeout
expiry of a paused JSON stream. Server admission returns to zero before the
consumer disposes its iterator. A subsequent call succeeds, and disposing the
old iterator does not change the new worker's process identity. All cases passed.
