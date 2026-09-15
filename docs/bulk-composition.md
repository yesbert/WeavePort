# Large results and external composition

Combine bounded plugin results under your application’s ordering and merge rules. `WeavePort.Composition` is an optional .NET package depending on Abstractions and is outside the four-package public 0.3.0 release. The product owns domain contracts, authentication, selected sessions, order and merge semantics. Plugins do not get peer addresses, result paths or authority to resolve arbitrary result handles. Existing invocation messages remain limited to 1 MiB; a logical result can span many bounded invocations.

## Request-owned results

Create one `ResultScope` per product request with an authenticated tenant and a host-private storage root. `CreateAsync` supplies a write-only quota-enforcing stream. Producers must await their writes, observe cancellation, avoid concurrent writes and never retain the stream. The result becomes visible only after successful completion and flush. A partial creation is deleted and its reservation released on failure. Explicit scope disposal cancels and waits for active operations, then removes all files. This relies on cooperative host producer code, not forced thread termination.

`ResultHandle` contains no public path or network token; it is an in-process immutable handle. Another scope cannot use it, even with the same tenant string. `IPluginSession.Tenant` exposes immutable host-bound identity. `Composition.MapAsync` rejects a session for another tenant before invocation; the product must still supply authenticated identities, correct versions/configuration and authorized plugin choices. Custom session implementations and host code are trusted.

Default limits are 256 MiB per object, 1 GiB per scope and 32 objects, including in-progress reservations. They are independently configurable. A three-way fan-in of 128 MiB results needs an object limit of at least 384 MiB and enough scope budget for input, branches and joined output simultaneously. Completed intermediates remain until scope disposal. Products must separately limit aggregate concurrent requests, tenant/node disk usage and deadlines. These limits do not allocate RAM and do not impose an OS sandbox on trusted native workers.

## Composition

```csharp
await using var results = new ResultScope(privateRoot, authenticatedTenant,
    new ResultLimits(MaximumObjectBytes: 512L << 20, MaximumScopeBytes: 2L << 30));
ResultHandle source = await results.CreateAsync(
    (destination, token) => searchResults.CopyToAsync(destination, token), requestAborted);

// Sessions were bound by the trusted product to authenticatedTenant.
ResultHandle enriched = await Composition.MapAsync(results, source, enrichment, 65536, requestAborted);
ResultHandle processed = await Composition.MapAsync(results, enriched, processing, 65536, requestAborted);
await results.CopyToAsync(processed, responseStream, requestAborted);
```

`MapAsync` implements the small `bulk-map` contract: input/output objects have a base64 `data` field. The host reads 4–256 KiB raw blocks, invokes the selected session once per block and writes each result before requesting another. The default block size is 64 KiB. Source-generated JSON serializes bytes without a caller-created base64 string; JsonElement decoding avoids another intermediate string. Process boundaries still require encoding, copying and parsing. This is not a zero-copy protocol. Output size is checked against the block bound; the underlying 1 MiB frame limit also bounds malformed-response allocation.

Use `TransformAsync` to supply another trusted adapter/operation contract. Its returned memory must remain valid until the next transform call or until the operation completes; do not return pooled output buffers early. The scope awaits writing that output before reusing its input buffer. A byte block is not necessarily a complete document or NDJSON record. The fixture's ASCII transformation permits split records; semantic record processors need their own bounded framing/state. Whole-list/global algorithms need product-defined ingestion/finalization or indexed access strategies; independent per-block ranking is not assumed equivalent to global ranking.

`FanOutAsync` accepts product-supplied branches and maximum parallelism. Every branch reads the immutable source through its own bounded buffer and produces independent output. On any required failure, the parallel executor cancels and awaits started siblings. Successful intermediate results remain scope-owned until disposal. No automatic retries occur; a failed plugin invocation may already have caused side effects.

`ConcatenateAsync` joins results in declared order. This is suitable for NDJSON byte streams, not arbitrary JSON arrays or semantic deduplication. The product can implement its own fan-in using `CreateAsync` and authorized reads. Optional branches, first-success wins and durable workflow replay are not implemented implicitly.

## Caller delivery and cancellation

`CopyToAsync` streams the committed result into the caller's stream with bounded buffering. It awaits destination writes, so a slow consumer applies backpressure. Request cancellation propagates through file I/O and plugin invocation; scope disposal waits for active I/O before removing data. Plugin sessions belong to the product and must be disposed separately. Native worker cleanup has the existing trusted-process limits.

The finite `WeavePort.BulkDemo http` test supplies a loopback ASP.NET Core endpoint and HttpClient receiver. The product handles routing/authentication and supplies RequestAborted to the composition call. The returned body is the completed result, not a handle-only response. Committing before delivery means this is not immediate token streaming from an LLM; a separate live-output contract would be needed for that behavior.

## Security and portability limits

File ownership and result-handle checks protect the broker API. They do not prevent hostile code running under the same native OS identity from bypassing the API and reading filesystem data. Use an independently enforced worker sandbox for untrusted plugins; never mount the private result root into it. Paths, data and the per-run HTTP test key are not logged. Secure physical erasure, encrypted result storage, crash-orphan reclamation and cross-node storage are not claimed by this optional package.

The implementation targets .NET 10 using portable file/stream/concurrency APIs; Unix private-directory modes are conditional. Windows requires the product to provide a root with appropriate ACLs. Actual new execution evidence is from macOS native socket/stdio; compile support is not Windows/Linux execution proof. See [regression entry points](../tests/README.md) and the [results report (historical) — pre-public record](history.md) for measured sizes and tradeoffs.
