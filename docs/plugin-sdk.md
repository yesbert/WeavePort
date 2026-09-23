# Authoring plugins with WeavePort

Write a plugin as ordinary asynchronous functions and result streams in C#, Python or TypeScript. Your application defines the contract; the SDK takes care of communicating with the host. WeavePort supplies dispatch, JSON conversion, local transport selection, callback framing, stream batching and lifecycle. The same provider artifact runs directly under a product's PluginHost or under the separate worker-host gateway. The gateway is one shared ASP.NET Core process, not a server inside every plugin.

## C#

Reference the public `WeavePort.Sdk` NuGet package at the version declared in the [compatibility matrix](package-compatibility.md), or the matching locally packed package when working from source. Register ordinary async handlers; the delegate return uses ValueTask, so an async lambda needs no wrapper. Asynchronous iterators use normal `IAsyncEnumerable<T>` and cancellation tokens.

```csharp
var plugin = new PluginApplication();
plugin.Function<SearchRequest, SearchResult>("search", async (request, context, token) =>
{
    // Application logic, including approved context.CallHostAsync capabilities.
    return await SearchAsync(request, token);
});
plugin.Stream<SearchRequest, SearchHit>("results", GetResultsAsync);
await plugin.RunAsync();
```

The executable [C# example](../examples/sdk/csharp/Program.cs) includes echo, authorized callbacks, a lazy record generator and explicit test diagnostics. Optional serializer options allow supplying generated JSON metadata; NativeAOT is not qualified. The default uses web JSON property conventions. There is no dependency on ASP.NET Core or gRPC in the author SDK.

## Python

Install the local `weaveport-sdk` wheel; runtime dependencies are Python's standard library.

```python
app = PluginApplication()

@app.function("search")
async def search(request, context):
    return await search_backend(request)

@app.stream("results")
async def results(request, context):
    async for item in search_pages(request):
        yield item

app.run()
```

See the [runnable Python example](../examples/sdk/python/plugin.py). Use ordinary async cancellation/finalization patterns. The SDK owns its protocol reader and callback sequencing.

## TypeScript

Install the packed `@weaveport/sdk` npm archive. The SDK includes JavaScript and type declarations, with no third-party runtime dependency.

```typescript
const plugin = new PluginApplication();
plugin.function<SearchRequest, SearchResult>('search', async (request, context, signal) =>
    searchBackend(request, signal));
plugin.stream('results', async function* (request: SearchRequest, context, signal) {
    for await (const item of searchPages(request, signal)) yield item;
});
await plugin.run();
```

See the [runnable TypeScript example](../examples/sdk/typescript/plugin.ts). Context properties are readonly; `callHost` provides typed callback results. The ordinary AbortSignal is used for SDK lifecycle cancellation.

## Artifact version

Declare the plugin's artifact version independently of protocol version. Existing callers default to `"1"`:

```csharp
var plugin = new PluginApplication { PluginVersion = "2" };
```

```python
app = PluginApplication(plugin_version="2")
```

```typescript
const plugin = new PluginApplication('2');
```

The declaration is sent in the startup handshake. The host's expected `PluginContext.Version` must match; a mismatch is rejected before calling a function. Empty/whitespace declarations are refused. Exclusive workers use protocol 1; explicitly concurrent workers use protocol 2. Authors set this from their build/release metadata, not from caller arguments or host request data. This identifies an artifact; it is not package signing or a compatibility negotiation scheme.

For an installed C# release, opt into build metadata explicitly:

```csharp
var plugin = new PluginApplication
{
    PluginVersion = PluginApplication.VersionFromAssembly()
};
```

The helper reads the entry assembly's `AssemblyInformationalVersionAttribute`, or an explicitly supplied plugin assembly. By default it removes the `+` build metadata suffix appended by Source Link, so `1.2.3-beta+commit` becomes `1.2.3-beta`; `includeBuildMetadata: true` retains the full informational version. Installed catalog release identifiers do not accept `+`, so use the default for installed plugins. Missing informational metadata is refused. Match the resulting string exactly to the sealed installation version; the four-part assembly binding version is not substituted. Omission of this helper still defaults to `"1"`.

A startup mismatch returns status `version-mismatch` and structured `VersionMismatch.Expected`/`Advertised` on `InvocationResult` or `PluginCallException`, with `MayHaveExecuted = false`. Shared startup and prewarming throw `PluginVersionMismatchException` with the same information in `Mismatch`. These values are not added to standard logs.

Run `./scripts/sdk-versions.sh` for packed C#/Python/TypeScript startup and call checks. The [Decision Room version walkthrough](../samples/DecisionRoom/README.md#parallel-plugin-versions) demonstrates application-owned version selection and journals.

## Product integration

Use `WeavePort.Sdk.Client` over an already authorized `IPluginSession`:

```csharp
IPluginClient plugin = new LocalPluginClient(session);
SearchResult result = await plugin.CallAsync<SearchRequest, SearchResult>("search", request, token);
await foreach (SearchHit hit in plugin.StreamAsync<SearchRequest, SearchHit>("results", request, token))
    await DisplayAsync(hit, token);
```

For a worker host, the trusted application registers that same local client in `GatewayRegistry`, adds gRPC services and maps `GatewayService`. Registration returns a random credential that authorizes only that preconfigured binding. The caller constructs `RemotePluginClient(endpoint, credential)` and uses the same IPluginClient methods. The binding client owns a reusable channel and up to eight binding-scoped duplex sessions to avoid a demonstrated Kestrel stream-reuse defect without per-call TCP churn; see [the cause and compatibility correction (historical) — pre-public record](history.md). No provider edit or gRPC import is necessary. RPC requests cannot register executable paths, select tenant identity or assign grants.

The package family separates `WeavePort.Sdk.Gateway` hosting (ASP.NET Core required) from `WeavePort.Sdk.Gateway.Client` (plain .NET). See [HTTPS deployment and package usage](gateway.md). The [worker-host sample](../tests/WeavePort.WorkerHost/Program.cs) binds loopback and configures 1-MiB gRPC limits. Its stdin bootstrap and snapshot commands belong to the test launcher, not to plugin authors or the public remote protocol. Deployment registration, credential distribution/rotation and remote TLS termination belong to the trusted application. HTTPS is required by the client for non-loopback endpoints; the exact executed TLS topology and deployment-site checks are documented in the gateway guide.

Callbacks execute **where PluginHost is hosted**. Supply the application's `IHostCallbacks` implementation there. The sample uses identical application callback code in both locations. This first gateway does not serialize or forward arbitrary closures from the calling product process. For a remote node, that callback implementation needs access to the appropriate application services. The plugin API remains `context.CallHostAsync`, `context.call_host` or `context.callHost`.

## Ownership, limits and cancellation

- A function returns one bounded value; a stream yields individual records. The SDK batches records internally (up to 16 records/256 KiB). One lookahead item is permitted; no whole-list accumulation is required in the SDK. An individual serialized item is limited to 128 KiB and a complete stream to 64 MiB. Unary client input/output is limited to 512 KiB; the underlying frame/message ceiling is 1 MiB. JSON encoding can expand values, so limits concern representation rather than string character counts. Oversized items fail explicitly; arbitrary giant records or binary-file streams are not automatically split by this first API.
- Exclusive streams retain a worker reservation for the entire enumeration. The host admission policy queues or refuses concurrent work. Approved shared unary bindings provide concurrent calls within one resident worker; products can also orchestrate independent bindings. Customer-bound workers never move between tenants. Explicitly approved native SDK deployments can reuse a worker after registered session cleanup; see [resource ownership and best practices](reusable-plugins.md).
- A local SDK client owns its supplied IPluginSession. Disposing it cancels and disposes that binding. A remote client cancels its outstanding calls on disposal, and disposes its retained sessions/channel; the trusted gateway application owns binding registration and revocation. Revoke credentials/dispose the registry to terminate server-side bindings.
- Early local iterator disposal attempts graceful generator closure. Active cancellation or cleanup uncertainty stops/restarts the owned worker through the existing host; native protocol v1 does not deliver a separate soft-cancel frame. Python/TypeScript/C# cleanup code is therefore not guaranteed to run after forced process termination. Remote early disposal cancels the stream and waits for a binding-scoped cleanup acknowledgement; it cannot cancel a different stream ID.
- Streams use `PluginStreamOptions`: a 30-second per-exchange deadline and a five-minute total deadline by default. The total includes consumer pauses. These are separate from unary call deadlines. Operation leases prevent eviction between batches. Available records flush immediately; empty unfinished batches are valid heartbeats. Cancellation/producer errors after some records mean partial output, not transactional success; products decide what to do with already consumed records. Do not retry external effects without application idempotency semantics.
- Callback IDs and grants remain host-owned. Invocation scopes reject late detached callbacks, and SDKs serialize wire writes; concurrent protocol dispatch keeps each invocation’s callback identity separate. The host's callback budget applies per internal invocation/batch; a generator doing many callbacks must respect the configured host policy. SDK context identity is information, not permission to access arbitrary customers.
- Normal console logging is redirected to stderr after the SDK runtime starts. Do not write raw protocol streams or emit stdout before runtime initialization. Raw stderr forwarding is an explicit operator option; verified manifests and approvals configure launch policy. Ordinary provider functions never select sockets, gRPC or batch sizes.

The [compatibility policy](package-compatibility.md) defines the exact core package surface; optional Gateway deployment remains separate. Native SDK workers remain explicitly trusted same-user processes. Windows/Linux and remote production deployment still require qualification. Build and verification instructions are in [the SDK harness guide](../tests/README.md); earlier measurements remain in [the SDK result report (historical) — pre-public record](history.md).

Use matching gateway package versions. The gateway wire protocol uses duplex exchanges. Session lease waits count against the operation timeout. An incomplete exchange is discarded, not returned to the session pool; every exchange rechecks the binding credential. Idle transport sessions remain until client disposal.

See [current performance evidence](../reports/benchmarks/current/README.md) for the current delivered core and optional loopback Gateway measurements.

## Per-call timing

```csharp
PluginCallResult<SearchResult> measured =
    await plugin.CallWithMetadataAsync<SearchRequest, SearchResult>("search", request);
SearchResult value = measured.Value;
double? elapsedMs = measured.ElapsedMs;
```

Untyped calls return `PluginCallResult<JsonElement>`. `ElapsedMs` is the host's monotonic `InvocationResult.ElapsedMs`, including host admission/startup and worker transport within that invocation. It excludes the outer gateway network round trip. Every concurrent call owns its result; there is no shared last-result state. Existing `CallAsync` remains value-only. Custom clients using the default interface implementation and older gateways without the optional timing field return `null`, never a fabricated zero. Failures still throw `PluginCallException`, preserve `MayHaveExecuted`, and must not be automatically retried merely because they failed.

## Session-owned resources

All three SDKs now provide registered cleanup and resource ownership for a function call or complete stream. Old contexts expire before cleanup, registered actions run in reverse order and cleanup failure retires an approved worker. This is cooperative cleanup, not a scan of every object or background task. Read [Writing plugins for approved session reuse](reusable-plugins.md) and the [multilingual examples](../examples/reuse/README.md) before enabling the operator-owned policy. Use the matching SDK artifacts declared by the installation compatibility policy.

## Shared functions and binary sources

Set C# `PluginApplication.ConcurrentCalls = true`, Python `PluginApplication(concurrent_calls=True)` or TypeScript `new PluginApplication('1', { concurrentCalls: true })` for the shared protocol. The operator must separately approve Shared ownership and choose a degree. C# async functions and Python async functions can overlap; Python synchronous functions use bounded worker threads. TypeScript CPU-bound work still needs author-managed worker threads. Shared mutable state must be concurrency-safe. Shared clients refuse streams and sources before dispatch. See [shared execution](shared-execution.md).

For plugin-originated files, C# `Source<TInput>` returns a readable `Stream`; Python `@app.source` returns a readable binary file; TypeScript `plugin.source` returns a `BinarySource` with `read(maxBytes)` and `close()` methods. The runtime owns source cleanup. `IBoundPluginClient.SourceAsync` reads bounded 4–256 KiB blocks under one exclusive lease; `Composition.CollectAsync` commits them into a quota-enforced result scope. JSON streams keep their existing 64 MiB limit. See [bulk composition](bulk-composition.md).
