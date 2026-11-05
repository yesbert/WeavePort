# Function files

**Availability:** current source after 0.9.1, not yet released. Local calls and the scheduler path are implemented. Calls through the gateway are not available yet; remote clients refuse them with `files-unsupported`.

A function call can take named file inputs from the host and hand named file outputs back, beside its bounded JSON input and result. The bytes pass only through readers and writers the host application supplies. The plugin never gets a storage location, a credential or a key. Typical use: a spreadsheet a person uploaded goes in, and a generated workbook comes out.

## Host side

The trusted application decides which files a call may use. For each call it supplies:

- **inputs**: a name, an exact length, a media type and an `IPluginInputReader`;
- **an output destination**, optionally: an `IPluginOutputDestination` that creates one `IPluginOutputWriter` per output the plugin creates;
- **limits** for this call.

```csharp
var sheet = new PluginInput("sheet", length, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", reader);
var files = new PluginFiles([sheet], destination, new PluginFileLimits(maximumOutputs: 2, maximumOutputBytes: 64L << 20));
PluginCallResult<ImportSummary> result = await client.CallWithFilesAsync<ImportRequest, ImportSummary>("sheet.extract", request, files, cancellationToken);
foreach (PluginOutputInfo output in result.Outputs)
{
    // name, media type and exact committed length
}
```

`CallWithFilesAsync` exists on `LocalPluginClient`. A host configured with `SchedulingOptions` carries files through its queue. Lower-level code can use `IPluginFileSession.InvokeAsync(operation, payload, files)`.

**Readers** are positional. WeavePort calls `ReadAsync(offset, buffer)` at any offset and in any order, never concurrently within one call, and never beyond the declared length. A reader returns fewer bytes than requested only at the end of the input. Storage that can only be decrypted forward must either support block-wise random access, for example with independently authenticated chunks, or be buffered by the application. WeavePort does not copy inputs to disk. Built-in readers: `PluginInputReader.FromStream` for a readable, seekable stream, and `PluginInputReader.FromFile` for an open `SafeFileHandle`. The caller keeps ownership of both.

**Writers** receive blocks in order. WeavePort calls exactly one of the following, and then `DisposeAsync`:

- `CommitAsync`, when the call succeeded and the plugin completed every output it created. Outputs are committed in creation order.
- `AbortAsync(reason)` otherwise, including for outputs the plugin had already completed. The reasons are `Failed`, `Cancelled`, `Limit` and `CommitFailed`.

Abort and disposal run under a bounded grace period even after cancellation. Their failures are reported as `PluginFailure.CleanupFailed` and do not replace the primary code. If committing a later output fails, the call fails with `output-commit-failed`, and `Outputs` lists the outputs that were already committed. WeavePort cannot undo a commit, so a destination that needs all-or-nothing across outputs makes them visible in a transaction of its own.

Built-in destinations: `PluginOutputDestination.ToDirectory` writes hidden partial files and moves each one to its output name on commit, never replacing an existing file; it is meant for tools and tests. `ResultScope.AsOutputDestination()` from `WeavePort.Composition` makes committed outputs scope results. See [bulk composition](bulk-composition.md).

## Limits and failures

| Limit | Default | Exceeded |
|---|---|---|
| Inputs per call | at most 64 | refused when `PluginFiles` is constructed |
| Outputs per call | `PluginFileLimits.MaximumOutputs`, at most 64; 0 permits none | call fails with `output-limit` |
| Total output bytes | `PluginFileLimits.MaximumOutputBytes` | call fails with `output-limit` before the excess reaches a writer |
| Input bytes delivered by readers | `MaximumInputReadBytes`, or four times the sum of the input lengths | call fails with `input-read-limit` before the reader is called |
| Block size | 4–256 KiB decoded, 64 KiB by default, chosen by the plugin SDK | protocol error |

Reads served from the SDK's block cache do not count against the read budget. All file transfer happens within the call's deadline, so raise the binding's timeout for large files.

Before the plugin runs, a call with files is refused with `files-unsupported` and `MayHaveExecuted = false` in these cases:

- the worker did not advertise file support (an SDK older than this change);
- the binding is shared or MCP;
- the operation is a stream or a source.

A call that returns while an output it created is still open fails with `output-incomplete`. Failures after dispatch keep `MayHaveExecuted = true`. A file failure retires the worker, like other failures inside an invocation.

These codes are distinct from the existing `input-limit`, which bounds the unary JSON request. See [failure codes](failure-codes.md).

## Plugin side

Only exclusive SDK workers support files; concurrent (shared) workers do not advertise the capability. Opening an unknown input, or creating an output the call does not permit, is refused locally before any request reaches the host. A read or write after the call ended is refused like a late callback. Each open input caches its four most recently read blocks, so archive readers that revisit their directory and headers cause no extra round trips.

**C#**

```csharp
plugin.Function<ImportRequest, ImportSummary>("sheet.extract", async (request, context, token) =>
{
    await using Stream sheet = context.OpenInput("sheet");            // seekable; synchronous Read also works
    using var archive = new ZipArchive(sheet, ZipArchiveMode.Read);
    await using PluginOutputStream result = await context.CreateOutputAsync("result.xlsx", MediaTypes.Xlsx, token);
    await WriteWorkbookAsync(archive, result, token);
    await result.CompleteAsync(token);                                // or rely on disposal
    return summary;
});
```

`context.Inputs` lists the descriptors. Refusals throw `PluginFileException` with `Code`. Set `PluginApplication.FileBlockBytes` to change the block size.

**Python**

```python
@app.function("sheet.extract")
async def extract(request, context):
    sheet = await context.open_input("sheet")
    rows = await asyncio.to_thread(read_rows, sheet.as_file())   # sync file for zipfile/openpyxl, off the event loop
    async with await context.create_output("result.xlsx", XLSX) as result:
        await result.write(render(rows))
    return dict(rows=len(rows))
```

`await sheet.read_at(offset, length)` reads asynchronously. `as_file()` raises on the event-loop thread instead of hanging. Refusals raise `PluginFileError` with `code`. Use `PluginApplication(file_block_bytes=...)` to change the block size.

**TypeScript**

```typescript
plugin.function('sheet.extract', async (request, context) => {
    const sheet = context.openInput('sheet');
    const tail = await sheet.read(sheet.size - 22, 22);
    const result = await context.createOutput('result.xlsx', XLSX);
    await result.write(render(tail));
    await result.close();
    return { bytes: result.written };
});
```

`createOutput` also offers `toWritableStream()`. Refusals throw `PluginFileError` with `code`. Use `new PluginApplication('1', { fileBlockBytes })` to change the block size.

## Boundaries

The plugin process necessarily sees plaintext, and a trusted native plugin could write it anywhere it can reach. WeavePort's guarantees are grants, limits, cleanup and cancellation at the protocol boundary, not containment of hostile code. See [security boundaries](security-architecture.md). Bytes travel as base64 JSON blocks, with the same cost as sources and callbacks. Local measurements are recorded in the change's [verification notes](../openspec/changes/add-function-file-transfer/verification.md). The wire format is described in the [protocol reference](protocol.md#function-files).
