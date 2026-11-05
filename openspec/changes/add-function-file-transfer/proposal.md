> **Status:** proposed; not implemented.

## Why

A function call can carry only bounded JSON: unary input and output are limited to 512 KiB. Consumers that hand a plugin a document (for example a 9 MB spreadsheet a person uploaded) or receive one back (an Excel file produced beside a JSON result) have no supported path. `Source` carries plugin-to-host bytes, but only as a separate operation, and `Composition.CollectAsync` commits them to plain files in a host directory. Nothing carries bytes from host to plugin. Consumers that keep files encrypted under a tenant key cannot give a plugin storage access or a key, and must not let plaintext bytes reach the host disk. Today they would rebuild chunking, grants, quotas, cleanup and cancellation over generic callbacks in each language. HiveWeaver requested this change (`weaveport-findings.md`, section "0.9.1, from the spreadsheet import", 2026-11-05). It has no deadline and no interim path.

## What Changes

- **Named inputs.** A host can name file inputs when it invokes a function. Each input has a name, a length and a media type, plus a host-side positional reader that the host supplies. The plugin opens an input by name and reads it at any offset: a seekable `Stream` in C#, a seekable binary file object in Python, and `read(position, length)` in TypeScript.
- **Named outputs.** A plugin can create named outputs, each with a media type, during a function call. The host supplies a writer per output, so it can transform the bytes (for example, encrypt them) as blocks arrive. An output is committed only when the call succeeds. On failure, cancellation or worker loss, every output is aborted through its writer.
- **Limits per invocation.** The host sets the maximum number of outputs and their total size. An output beyond either limit fails the call explicitly. Block sizes are the same as for `Source` (4–256 KiB decoded).
- **Capability handshake.** A worker advertises file support in its ready envelope. A host refuses a file-carrying call to a worker without that capability before dispatch, and to shared (protocol 2) or MCP bindings.
- **All three SDKs, both clients.** The feature is available in the C#, Python and TypeScript SDKs, in `LocalPluginClient`, and through the gateway. Inputs are read and outputs written at the remote caller over the existing duplex session. The gateway applies operator-registered maximum limits.
- **Built-in readers and writers.** Readers are provided for a seekable stream or a file. A `ResultScope` writer lets bulk composition remain one destination among others.
- One contract change through `contracts/protocol.json`, so every SDK and the compatibility matrix change in one release.

Non-goals: files in result streams, sources or MCP tools; shared or concurrent workers; encryption, storage, retention or authorization of files inside WeavePort; resumable or partial uploads; byte-range writes or seeking in outputs; zero-copy transfer (blocks stay base64 JSON); deciding which tool arguments are files (a consumer convention such as a JSON-schema `format`); raising unary JSON limits; hostile-plugin containment.

## Capabilities

### New Capabilities
- `plugin-function-files`: Host-supplied, positionally readable inputs and host-destined, transactional outputs for SDK function calls, with per-invocation limits, a capability handshake and gateway support.

### Modified Capabilities
- `bulk-composition`: Adds a request-scoped result destination for function outputs. Existing composition behaviour does not change.

## Impact

Contract and generated constants (`contracts/protocol.json`, three SDKs); the native worker exchange in `WeavePort.Hosting`; new public types in `WeavePort.Abstractions` and `WeavePort.Sdk.Client`; additive gateway protobuf fields and modes; `WeavePort.Composition`; public API baselines, the compatibility matrix and the protocol, SDK, gateway, failure-code and composition guides. The ready capability and invoke fields are additive: existing workers and callers keep working without them. Existing protocol numbers do not change. Planned for a minor release (0.10.0). This is proposed behaviour, not a release guarantee.
