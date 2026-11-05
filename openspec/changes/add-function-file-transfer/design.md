## Context

See proposal.md for the request and scope. Relevant current behaviour:

- **Callbacks.** An exclusive native worker (protocol 1) exchanges one invocation at a time. During an invocation the plugin may send `callback` frames that carry the invocation `id` and a unique `callbackId`. The host answers each with a `callback-result`. `PluginSession.Exchange` checks the grant and the per-invocation callback budget (default eight), runs the callback under callback admission, and rejects late or duplicate callbacks. SDKs serialize callback writes behind one gate.
- **Sources.** `Source` blocks are host-pulled `$sdk.source.read` invocations with 4–256 KiB decoded base64 data. They need an operation lease and are refused on shared clients.
- **Gateway.** The gateway is one gRPC duplex `Session` per lease: `Request` messages (operation, input, mode, chunk_bytes) and `Reply` messages (json, data, complete, error and failure fields). Callbacks run where `PluginHost` runs, not at the remote caller.
- **Composition.** `ResultScope` owns tenant-bound, quota-enforced files that become visible only after a successful completion.

## Goals / Non-Goals

**Goals.** Give a function call named inputs and outputs whose bytes pass only through consumer-supplied readers and writers. Keep authority with the host, offer the same API shape in three languages, apply explicit limits, and discard all output on any unsuccessful call. Keep `MayHaveExecuted` semantics unchanged. Existing workers, callers and the gateway keep working unchanged.

**Non-goals.** See proposal.md. In particular, WeavePort gives no confidentiality guarantee: the consumer's reader and writer own encryption, and the plugin process necessarily sees plaintext.

## Decisions

### File operations ride the callback direction as reserved SDK operations

A plugin pulls input blocks and pushes output blocks. Both are plugin-to-host requests within one invocation, which is the direction and correlation that callbacks already have. The SDK sends them as `callback` frames with reserved operations:

| Operation | Payload | Reply value |
|---|---|---|
| `$sdk.input.read` | `name`, `offset` (int64 ≥ 0), `length` (4 KiB–256 KiB) | `data` (base64, may be shorter at end, empty at or past end) |
| `$sdk.output.create` | `name`, `mediaType` | `{}` |
| `$sdk.output.write` | `name`, `data` (base64, 1 B–256 KiB decoded) | `{}` |
| `$sdk.output.complete` | `name` | `length` |

The host recognizes the `$sdk.` prefix before grant lookup. These operations do not consume the application callback budget or callback admission, and are never forwarded to `IHostCallbacks`. They run inline in the exchange loop under the invocation's cancellation token and deadline. Each reply uses the existing `callback-result` correlation, and a refusal is a `callback-result` error with a code. Reusing the existing frames keeps invocation scoping, late-call rejection, duplicate-ID checks and SDK write serialization exactly as they are.

**Alternatives.**
- New frame kinds: rejected. They would duplicate the correlation and scoping that callbacks already have.
- Host-pushed blocks as further invocations, like sources: rejected. An input must be readable while the function is running, which is not possible while the single exchange is occupied. Pushing output would also invert control for outputs.
- Generic application callbacks: rejected by the requester. They leave grants, quotas and cleanup as a consumer convention in each language.

### File descriptors are host authority in the invoke envelope

When a call carries files, the `invoke` envelope gets an additive `files` object:

```json
"files": {"inputs": [{"name": "sheet", "length": 9437184, "mediaType": "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"}],
          "outputs": {"maximumCount": 4, "maximumBytes": 67108864}}
```

The `files` object lives beside `context`, not in the application payload, so a plugin cannot create its own grant. An absent `outputs` object means the call allows no outputs.

Names follow these rules:

- 1–128 characters from `[A-Za-z0-9._-]`, not starting with `.`.
- Unique per invocation within inputs and within outputs. An input and an output may share a name.
- At most 64 inputs and 64 outputs per invocation (`MaximumFileInputs`, `MaximumFileOutputs`), which keeps the descriptors well inside one frame.
- Never interpreted as paths.

`length` is mandatory and fixed for the invocation. The plugin cannot read past it. Media types are opaque, bounded strings (≤ 255) that the host passes through. New reserved envelope fields go into the duplicate-field check.

### Capability handshake

A worker whose SDK supports files adds `files: 1` to its ready envelope. This is additive, like `sessionCleanup`. A host asked to invoke with files refuses before dispatch, with `MayHaveExecuted = false`, in these cases:

- the worker did not advertise the capability;
- the binding is shared (protocol 2);
- the binding is MCP;
- the operation is a stream or source.

The refusal code is `files-unsupported`. A new SDK on an old host simply receives no `files` object. There, `OpenInput` fails as "unknown input" and `CreateOutput` fails as "outputs not permitted", both locally and before any frame is sent. The native protocol number does not change.

**Alternative.** Bumping the protocol to 3 was rejected. The extension is optional per call and composes with protocol 1, as session cleanup does.

### Host API

The new types live in `WeavePort.Abstractions`:

- `PluginInput(string Name, long Length, string MediaType, IPluginInputReader Reader)` describes one input.
- `IPluginInputReader.ReadAsync(long offset, Memory<byte> buffer, CancellationToken)` is a positional read in the style of `RandomAccess.ReadAsync`. It may return fewer bytes than requested only at the end. The host may call it with any offset in any order, but never concurrently within one invocation.
- `IPluginOutputDestination.CreateAsync(name, mediaType, ct)` returns an `IPluginOutputWriter`. The writer has `WriteAsync(ReadOnlyMemory<byte>, ct)`, `CommitAsync(ct)`, `AbortAsync(PluginOutputAbort reason, ct)` and `DisposeAsync`.
- `PluginFileLimits(int maximumOutputs, long maximumOutputBytes, long? maximumInputReadBytes = null)`. Null means four times the sum of the declared input lengths. `PluginFileLimits.InputsOnly` permits no outputs.
- `PluginFiles(IEnumerable<PluginInput> inputs, IPluginOutputDestination? outputs = null, PluginFileLimits? limits = null)` copies the inputs, rejects duplicate names and more than 64 inputs, and requires limits permitting at least one output when a destination is given. `InputReadBudget` is the effective budget, saturating at `long.MaxValue`.

The constructors of `PluginInput`, `PluginFileLimits` and `PluginFiles` validate names, media types, lengths and limits, so an invalid descriptor never reaches the wire.

A new optional interface, `IPluginFileSession : IPluginSession`, adds `InvokeAsync(operation, payload, PluginFiles, ct)`. `IPluginSession` does not change, which avoids breaking existing implementations. `InvocationResult` gets an additive `Outputs` property: a list of `PluginOutputInfo(Name, MediaType, Length)` that is empty unless the call succeeded.

`WeavePort.Sdk.Client` adds `CallWithFilesAsync<TInput, TOutput>(operation, input, PluginFiles, ct)` on bound clients. It returns `PluginCallResult<T>` with `Outputs`. A client without file support throws `PluginCallException` with `files-unsupported` before dispatch.

Built-in implementations:

- `PluginInputReader.FromStream(Stream)` requires `CanSeek` and serializes access.
- `PluginInputReader.FromFile(path)` uses `RandomAccess`.
- `PluginOutputDestination.ToDirectory(root)` is for tests and tools.
- `ResultScope.AsOutputDestination()` lives in Composition.

**Positional reads are a consumer prerequisite.** A consumer whose storage format allows only forward decryption must either support block-wise random access, for example with independently authenticated chunks, or buffer the file itself. WeavePort does not copy inputs to disk on the consumer's behalf.

### Output lifecycle is two-phase and transactional

The plugin side of the lifecycle:

- `create` calls the destination.
- `write` appends sequentially. The host checks the count and total-byte limits before calling the writer, and refuses an excess with `output-limit`.
- `complete` closes the plugin side.

When the invocation ends:

- **Successful result and every created output completed:** the host calls `CommitAsync` on each writer in creation order, then returns `ok` with `Outputs`.
- **Successful result but an output not completed:** the call fails with `output-incomplete`.
- **Any other outcome:** error, cancellation, timeout, worker loss, protocol failure, a limit or a commit failure.

In every failure case, the host calls `AbortAsync` once on every created writer, including completed ones, and then disposes all writers. It passes a reason (`failed`, `cancelled`, `limit`, `commit-failed`). Abort failures are aggregated as cleanup failures (`PluginFailure.CleanupFailed`) and do not replace the primary code.

A commit failure after some commits succeeded is reported as `output-commit-failed` with `MayHaveExecuted = true`. The remaining writers are aborted, and outputs that were already committed are listed as committed in the failure. WeavePort cannot roll back a destination that has already committed. A destination that needs all-or-nothing behaviour commits in its own transaction; `ResultScope` does so.

Abort and dispose run bounded by a cleanup grace period, even after the invocation token has been cancelled.

**Alternatives.**
- Commit on `complete`: rejected. A later failure in the call would leave orphaned outputs.
- Making the plugin result list its outputs: rejected. The host already knows exactly which outputs were completed, and a plugin-supplied list would be a second, forgeable source.

### SDK surfaces

**C#.** `context.Inputs` lists the descriptors. `context.OpenInput(name)` returns a read-only seekable `Stream` with `Length`, backed by the shared block cache below. `context.CreateOutput(name, mediaType)` returns a write-only, non-seekable `Stream`. Disposing it completes the output, and `CompleteAsync` reports errors explicitly. An output that was never completed fails the call at return.

**Python.** `context.inputs` lists the descriptors. `await context.open_input(name)` returns an `InputFile` with:

- `async read_at(offset, length)`;
- `as_file()`, a synchronous `io.RawIOBase` that is seekable and buffered by `io.BufferedReader`, for libraries such as `zipfile` and `openpyxl`. It must be used off the event-loop thread (for example with `asyncio.to_thread`); on the loop thread it raises. This follows the existing `call_host_sync` pattern.

`await context.create_output(name, media_type)` returns an `OutputFile` with `async write` and `async close`, a sync `as_file()`, and `async with` support.

**TypeScript.** `context.inputs` lists the descriptors. `context.openInput(name)` returns `{ size, read(position, length): Promise<Uint8Array> }`. `context.createOutput(name, mediaType)` returns `{ write(chunk): Promise<void>, close(): Promise<void> }` plus a `toWritableStream()` adapter.

**All languages.** Block size is chosen by the SDK: 64 KiB by default, configurable within 4–256 KiB.

Each open input keeps a small cache of recently read, block-aligned blocks: four by default, least recently used evicted, so at most 1 MiB per input at 256 KiB blocks. ZIP readers revisit the central directory, local headers and data. Those revisits are then served without another round trip. The cache needs no concurrency, no protocol support and no new failure cases. Reads and writes are serialized through the existing callback gate. A read or write after the invocation ended raises the same expired-invocation error as a late callback. Registered cleanup closes any open files.

### Gateway

The gateway needs a protocol revision because the reader and writer live at the remote caller.

**Requests.** A new `Mode.FILE_CALL` request carries the operation, input JSON and file descriptors. The descriptors contain names, lengths, media types and requested limits, but no bytes.

**Server-side relay.** The server builds a `PluginFiles` whose readers and writers relay over the same duplex stream:

- Each read becomes a `Reply` with `file_request {kind: READ, name, offset, length}`, answered by a `Request` with `file_data`.
- Output create, write, commit and abort become `Reply` file requests. Write requests carry `data`. Each is answered by an acknowledgement `Request`.

The exchange still ends with the normal final `Reply`.

**Limits and failures.**

- `GatewayRegistry.Register` gets a `PluginFileLimits` maximum per registration, defaulting to no files. A request above the operator maximum is refused with `files-unsupported` or `output-limit` before dispatch, so a credential holder cannot raise limits.
- Remote readers and writers see the same lifecycle. A transport failure during the exchange aborts all outputs at the client and reports `MayHaveExecuted` according to the existing rules.
- All protobuf fields are additive. An old server answers `FILE_CALL` as an unknown mode, and the client maps that to `files-unsupported`.

The default remote block size is 256 KiB, to reduce round trips. A 9 MB input takes about 36 read round trips.

**Alternative.** A short-lived download URL for the plugin was rejected by the requester. It would be a second channel with a leakable token.

### Limits and codes

New failure codes, added to `contracts/protocol.json` and the failure catalogue:

| Code | Meaning | `MayHaveExecuted` |
|---|---|---|
| `files-unsupported` | Refused before dispatch | false |
| `output-limit` | An output limit was exceeded | true |
| `input-read-limit` | The input read budget was exceeded | true |
| `output-incomplete` | An output was created but not completed | true |
| `output-commit-failed` | Committing an output failed | true |

`input-read-limit` is distinct from the existing `input-limit`, which bounds the unary request size. Codes inside `callback-result` errors for the plugin: `input-not-found`, `output-not-permitted`, `output-exists`, `output-closed`, `invalid-range`.

**Input read budget (decided 2026-11-05).** The host counts the bytes its readers deliver per invocation, across all inputs. Reads served from the SDK's block cache never reach the host and are not counted. A read that would exceed `MaximumInputReadBytes` is refused before the reader is called, and the call fails with `input-read-limit`. The default is four times the sum of the declared input lengths. That leaves room for ZIP libraries that revisit ranges, while stopping a plugin that loops over the same file until its deadline. It guards against a faulty plugin, not a hostile one, in line with the trusted native boundary.

Through the gateway the registration caps the budget like the output limits.

Reads are further bounded by block size, the declared length and the invocation deadline, which already includes callbacks. Consumers moving large files should raise the unary call timeout (default 30 s on the gateway client).

## Risks / Trade-offs

- **Base64 in JSON costs about a third more bytes and parsing work.** The feature matches the cost of `Source` and callbacks. Performance is recorded, not promised, in the evidence report.
- **Round trips per block.** Reads are sequential within an invocation. Latency grows with the block count, especially through the gateway. Mitigations are larger blocks (256 KiB by default through the gateway) and the SDK's block cache.
- **Positional reads require a random-access consumer format.** This is documented as a prerequisite.
- **Partial commit across destinations cannot be undone by WeavePort.** It is reported explicitly, with the committed names listed.
- **Plaintext in the plugin process.** The plugin necessarily sees plaintext, and a trusted native plugin could write it to disk itself. This is documented under the existing trusted-plugin boundary, not prevented.
- **Python sync file used on the loop thread would deadlock.** It is refused with a clear error rather than allowed to hang.

### No read-ahead in this change

Decided on 2026-11-05. The SDKs do not read ahead, and the host API gets no prefetch hint.

Reads within an invocation stay sequential through the callback gate. Read-ahead could therefore only overlap the transfer of the next block with the plugin's processing of the current one. That saves at most one round trip per block, and only when reading forward. Locally that is negligible. Through the gateway, at an assumed 10 ms round trip, it is about 0.36 s for a 9 MB input.

In return, read-ahead would need the following in all three SDKs:

- an unrequested read in flight, which has to be drained or discarded before any result, error, output write or application callback;
- new cancellation and cleanup cases, where a failed unused block must not fail the call;
- wasted storage fetches and decryption at the consumer for the jump-heavy access patterns of ZIP files.

Task 6.1 measures the plugin's time per block and the round-trip time, locally and through the gateway. If the measurements show that gateway round trips dominate, SDK-internal read-ahead becomes a separate change. It needs no public API change. A host-side `PrefetchAsync` hint was rejected: a consumer can optimize its own storage access inside its reader.

## Implementation notes (host, block 2)

- **Worker retirement on file failures.** A call that fails with `output-limit`, `input-read-limit`, `output-incomplete` or `output-commit-failed` takes the existing failure path, which stops the worker. For the limit codes this is required, because the plugin is still inside the invocation. After a commit failure the worker would still be usable; retiring it anyway keeps one failure path.
- **Scheduler.** `ScheduledPlugin` implements `IPluginFileSession` and carries `PluginFiles` through the queue. Without it, a host configured with `SchedulingOptions` would refuse every file call.
- **Built-in reader for files.** `PluginInputReader.FromFile` takes an open `SafeFileHandle` instead of a path, so that the caller owns opening, sharing and closing.
- **Refusals by the host.** A write or complete for an output name that was never created is refused with `output-not-permitted`. A reader that returns fewer bytes than the declared length before its end fails the call with `failed`.
- **Callback replies with an error.** `callback-result` frames gain an optional `error` code. All three SDKs already turn a present `error` into an exception; the SDK changes in block 3 map the codes.

## Consumer confirmation

HiveWeaver reviewed commit `43c53be` on 2026-11-05 and confirmed the design without asking for changes.

- **Positional reads.** HiveWeaver moves its storage to a chunked format with independently authenticated blocks. Today it uses Stratara's `ISecureBlobEncryptor`, which is one AES-GCM block per payload. Its reader then serves `ReadAsync(offset, ...)` with ranged reads from object storage. WeavePort keeps not copying inputs to disk.
- **Two-phase outputs** are sufficient. HiveWeaver's `CommitAsync` records a file as unused, and the file becomes visible only through the call's completion. A partial commit leaves unused files that expire on their own. Aborting completed outputs and listing committed ones is what HiveWeaver needs.
- **Gateway.** HiveWeaver calls through the local `PluginHost` today, so the gateway path is not exercised by this consumer yet. It stays in scope because the original request asked for it.

## Migration Plan

Additive. Hosts upgrade first. Plugins gain the capability by rebuilding against the new SDK, and existing installations stay valid without the capability. Consumers adopt the feature by implementing a reader and a destination and calling `CallWithFilesAsync`. Gateway servers and clients must be upgraded together before `FILE_CALL` is used. No reseal is required by the protocol itself, but the release's compatibility matrix changes as usual.

## Open Questions

None open. The three questions from the first draft were decided on 2026-11-05: no read-ahead (see that section), an input read budget (see Limits and codes), and files only for functions (see below).

## Follow-up candidates

- **Files for result streams (`$sdk.start`).** The likely case is an input read and delivered as JSON records, for example a CSV import with progress.
- **Files for sources.** This one has little value of its own: a function with an output already covers a file-to-file transformation.

Both need further decisions:

- how long an input remains readable across many exchanges and consumer pauses;
- when outputs are committed if the consumer exits early;
- how the read budget and the stream's total deadline interact.

**Design rule for this change.** The `files` envelope object, the reserved `$sdk.input.*` and `$sdk.output.*` operations and the host types must not depend on `$sdk.call`. They are validated and handled per invocation, so that a later change can admit `$sdk.start` without a new wire format. The refusal of streams and sources with `files-unsupported` is a policy check in front of that shared path.
