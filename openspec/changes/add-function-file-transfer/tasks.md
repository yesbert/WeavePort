## 1. Contracts

- [x] 1.1 Add reserved operations, wire fields (`files`, `inputs`, `outputs`, `name`, `mediaType`, `length`, `offset`, `maximumCount`, `maximumBytes`), the ready capability `files`, failure codes and limits to `contracts/protocol.json`. Regenerate the constants in all three SDKs and extend the independent wire fixtures.
- [x] 1.2 Define the public .NET types (`PluginInput`, `IPluginInputReader`, `IPluginOutputDestination`, `IPluginOutputWriter`, `PluginOutputAbort`, `PluginFileLimits`, `PluginFiles`, `PluginOutputInfo`, `IPluginFileSession`, the `InvocationResult.Outputs` property and `CallWithFilesAsync`). Update the API baselines without changing existing members.
- [x] 1.3 Specify name, media type and length validation, and the duplicate reserved-field checks for the new envelope fields.

## 2. Host exchange

- [x] 2.1 Recognize reserved `$sdk.` callback operations before grant lookup. Exclude them from the callback budget and callback admission, and run them inline under the invocation token and deadline.
- [x] 2.2 Implement positional input reads with range and length checks, and the per-invocation read budget (`input-read-limit`, default four times the declared lengths).
- [x] 2.3 Implement output create, write and complete with count and total-byte limits.
- [x] 2.4 Implement the two-phase commit and abort lifecycle across success, plugin error, cancellation, timeout, worker loss, protocol failure, limit breach, an incomplete output and commit failure. Aggregate cleanup failures under a bounded grace period.
- [x] 2.5 Refuse file calls before dispatch with `files-unsupported` when the worker lacks the capability, or the binding is shared or MCP, or the operation is a stream or source. Verify `MayHaveExecuted = false`. Keep the stream and source refusal a policy check in front of an invocation-generic file path; it must not depend on `$sdk.call`.
- [x] 2.6 Provide the built-in readers and writers (`FromStream`, `FromFile`, `ToDirectory`) and `LocalPluginClient.CallWithFilesAsync`.

## 3. SDKs

- [ ] 3.1 C#: `context.Inputs`, `OpenInput` (a seekable `Stream` over the block cache), `CreateOutput` (complete on dispose, fail on return if not completed), late-access refusal and cleanup registration. Advertise `files: 1`.
- [ ] 3.2 Python: `context.inputs`, `open_input` with `read_at` and an off-loop sync `as_file()`, `create_output` with async and sync writers, and the loop-thread refusal. Advertise `files: 1`.
- [ ] 3.3 TypeScript: `context.inputs`, `openInput(...).read(position, length)`, `createOutput` with a `WritableStream` adapter, and AbortSignal integration. Advertise `files: 1`.
- [ ] 3.4 In each language, implement the block cache for inputs (four blocks by default, least recently used evicted) and verify that repeated reads of cached ranges send no frames.
- [ ] 3.5 In each language, verify against an old host that has no `files` object: a local refusal and no frames sent.

## 4. Gateway

- [ ] 4.1 Add the additive protobuf mode and fields for `FILE_CALL`, file requests, data and acknowledgements. Update the optional API baseline.
- [ ] 4.2 Relay reads and writes over the duplex session at the server, and apply the registration's maximum `PluginFileLimits`, with no files by default.
- [ ] 4.3 Implement `RemotePluginClient.CallWithFilesAsync`, with client-side abort on transport failure or cancellation. Map an old server's unknown-mode reply to `files-unsupported`.
- [ ] 4.4 Verify credential-scoped limits, cross-tenant refusal and revocation during a file call.

## 5. Composition

- [ ] 5.1 Add `ResultScope.AsOutputDestination()`: quota-enforced, visible only after commit, deleted on abort. Verify that outputs from a failed call never become visible.

## 6. Evidence and documentation

- [ ] 6.1 For each SDK and for both the local and the gateway path, read the central directory of a ZIP of about 9 MB at its end and produce an output of about 9 MB with a byte-exact hash. Show that SDK memory stays within its block bounds. Record the plugin's time per block, the round-trip time and the cache hit rate in a report, and claim no performance target. That report is the basis for deciding later on read-ahead.
- [ ] 6.2 Prove that output is discarded through the consumer's writer in every failure category from 2.4, that limits are refused explicitly, and that reads and writes after the invocation ended are refused.
- [ ] 6.3 Update `docs/protocol.md`, `docs/plugin-sdk.md`, `docs/gateway.md`, `docs/failure-codes.md`, `docs/bulk-composition.md`, `docs/status.md` and the examples. Document the positional-reader prerequisite and the plaintext-in-plugin boundary.
- [ ] 6.4 Run the focused tests, the public-tree, architecture, style and strict OpenSpec checks, and the normal qualification workflow. Record the verification, synchronize the deltas and archive the change. Notify HiveWeaver through its findings file.
