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
- `PluginFileLimits(int MaximumOutputs, long MaximumOutputBytes)`.
- `PluginFiles(IReadOnlyList<PluginInput> Inputs, IPluginOutputDestination? Outputs, PluginFileLimits Limits)`.

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

**C#.** `context.Inputs` lists the descriptors. `context.OpenInput(name)` returns a read-only seekable `Stream` with `Length` and one cached block. `context.CreateOutput(name, mediaType)` returns a write-only, non-seekable `Stream`. Disposing it completes the output, and `CompleteAsync` reports errors explicitly. An output that was never completed fails the call at return.

**Python.** `context.inputs` lists the descriptors. `await context.open_input(name)` returns an `InputFile` with:

- `async read_at(offset, length)`;
- `as_file()`, a synchronous `io.RawIOBase` that is seekable and buffered by `io.BufferedReader`, for libraries such as `zipfile` and `openpyxl`. It must be used off the event-loop thread (for example with `asyncio.to_thread`); on the loop thread it raises. This follows the existing `call_host_sync` pattern.

`await context.create_output(name, media_type)` returns an `OutputFile` with `async write` and `async close`, a sync `as_file()`, and `async with` support.

**TypeScript.** `context.inputs` lists the descriptors. `context.openInput(name)` returns `{ size, read(position, length): Promise<Uint8Array> }`. `context.createOutput(name, mediaType)` returns `{ write(chunk): Promise<void>, close(): Promise<void> }` plus a `toWritableStream()` adapter.

**All languages.** Block size is chosen by the SDK: 64 KiB by default, configurable within 4–256 KiB. Reads and writes are serialized through the existing callback gate. A read or write after the invocation ended raises the same expired-invocation error as a late callback. Registered cleanup closes any open files.

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
| `output-incomplete` | An output was created but not completed | true |
| `output-commit-failed` | Committing an output failed | true |

Codes inside `callback-result` errors for the plugin: `input-not-found`, `output-not-permitted`, `output-exists`, `output-closed`, `invalid-range`.

There is no separate input read budget. Reads are bounded by block size, the declared length and the invocation deadline, which already includes callbacks. Consumers moving large files should raise the unary call timeout (default 30 s on the gateway client).

## Risks / Trade-offs

- **Base64 in JSON costs about a third more bytes and parsing work.** The feature matches the cost of `Source` and callbacks. Performance is recorded, not promised, in the evidence report.
- **Round trips per block.** Reads are sequential within an invocation. Latency grows with the block count, especially through the gateway. Mitigations are larger blocks and the SDK's one-block read cache. Read-ahead is a possible later addition.
- **Positional reads require a random-access consumer format.** This is documented as a prerequisite.
- **Partial commit across destinations cannot be undone by WeavePort.** It is reported explicitly, with the committed names listed.
- **Plaintext in the plugin process.** The plugin necessarily sees plaintext, and a trusted native plugin could write it to disk itself. This is documented under the existing trusted-plugin boundary, not prevented.
- **Python sync file used on the loop thread would deadlock.** It is refused with a clear error rather than allowed to hang.

## Migration Plan

Additive. Hosts upgrade first. Plugins gain the capability by rebuilding against the new SDK, and existing installations stay valid without the capability. Consumers adopt the feature by implementing a reader and a destination and calling `CallWithFilesAsync`. Gateway servers and clients must be upgraded together before `FILE_CALL` is used. No reseal is required by the protocol itself, but the release's compatibility matrix changes as usual.

## Open Questions

- Should `IPluginInputReader` offer an optional read-ahead hint (`PrefetchAsync`), or should that wait for measurements?
- Is a total read byte budget per invocation needed for operators, or is the deadline enough? Proposed: deadline only, revisit with evidence.
- Should outputs be allowed for source or stream operations later? Out of scope here; the envelope is shaped so that it would not block that.
