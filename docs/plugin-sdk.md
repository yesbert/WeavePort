# Authoring plugins with WeavePort

The author writes functions and asynchronous result streams. WeavePort supplies dispatch, JSON conversion, local transport selection, callback framing, stream batching and lifecycle. The same provider artifact runs directly under a product's PluginHost or under the separate worker-host gateway. The gateway is one shared ASP.NET Core process, not a server inside every plugin.

## C#

Reference the locally packed `WeavePort.Sdk` NuGet package. Register ordinary async handlers; the delegate return uses ValueTask, so an async lambda needs no wrapper. Asynchronous iterators use normal `IAsyncEnumerable<T>` and cancellation tokens.

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

The declaration is sent in the startup handshake. The host's expected `PluginContext.Version` must match; a mismatch is rejected before calling a function. Empty/whitespace declarations are refused. The wire protocol remains version 1. Authors set this from their build/release metadata, not from caller arguments or host request data. This identifies an artifact; it is not package signing or a compatibility negotiation scheme.

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

The optional `WeavePort.Sdk.Gateway` NuGet contains both gateway hosting and its .NET client and requires the ASP.NET Core shared framework. Splitting client-only deployment dependencies is a possible later packaging refinement. The [worker-host sample](../tests/WeavePort.WorkerHost/Program.cs) binds loopback and configures 1-MiB gRPC limits. Its stdin bootstrap and snapshot commands belong to the test launcher, not to plugin authors or the public remote protocol. Deployment registration, credential distribution/rotation and remote TLS termination belong to the trusted application. HTTPS is required by the client for non-loopback endpoints; real remote deployment is not qualified by the current release.

Callbacks execute **where PluginHost is hosted**. Supply the application's `IHostCallbacks` implementation there. The sample uses identical application callback code in both locations. This first gateway does not serialize or forward arbitrary closures from the calling product process. For a remote node, that callback implementation needs access to the appropriate application services. The plugin API remains `context.CallHostAsync`, `context.call_host` or `context.callHost`.

## Ownership, limits and cancellation

- A function returns one bounded value; a stream yields individual records. The SDK batches records internally (up to 16 records/256 KiB). One lookahead item is permitted; no whole-list accumulation is required in the SDK. An individual serialized item is limited to 128 KiB and a complete stream to 64 MiB. Unary client input/output is limited to 512 KiB; the underlying frame/message ceiling is 1 MiB. JSON encoding can expand values, so limits concern representation rather than string character counts. Oversized items fail explicitly; arbitrary giant records or binary-file streams are not automatically split by this first API.
- One client binding is single-flight for the entire enumeration. A second concurrent call fails busy. Multiple independent bindings provide parallelism; products orchestrate serial or fan-out/fan-in work. Used workers never move between tenants.
- A local SDK client owns its supplied IPluginSession. Disposing it cancels and disposes that binding. A remote client cancels its outstanding calls on disposal, and disposes its retained sessions/channel; the trusted gateway application owns binding registration and revocation. Revoke credentials/dispose the registry to terminate server-side bindings.
- Early local iterator disposal attempts graceful generator closure. Active cancellation or cleanup uncertainty stops/restarts the owned worker through the existing host; native protocol v1 does not deliver a separate soft-cancel frame. Python/TypeScript/C# cleanup code is therefore not guaranteed to run after forced process termination. Remote early disposal cancels the stream and waits for a binding-scoped cleanup acknowledgement; it cannot cancel a different stream ID.
- Streams have a default 30-second overall local client deadline and existing per-invocation host deadlines. Remote calls and streams also have a configurable 30-second client timeout covering connection/response waits, including an unresponsive gateway; uncertain stream cleanup adds at most five seconds. The gateway uses the same local client and policies. Keep host idle eviction disabled (the default) or longer than the stream lifetime: the current pull protocol does not pin a worker between batches. Cancellation/producer errors after some records mean partial output, not transactional success; products decide what to do with already consumed records. Do not retry external effects without application idempotency semantics.
- Callback IDs and grants remain host-owned. Invocation scopes reject late detached callbacks, and SDKs serialize callback exchanges. The host's callback budget applies per internal invocation/batch; a generator doing many callbacks must respect the configured host policy. SDK context identity is information, not permission to access arbitrary customers.
- Normal console logging is redirected to stderr after the SDK runtime starts. Do not write raw protocol streams or emit stdout before runtime initialization. Logging, custom schemas, discovery/manifests and stronger sandbox adapters need further productization; ordinary provider functions never select sockets, gRPC or batch sizes.

The [compatibility policy](package-compatibility.md) defines the exact internal core surface; optional Gateway deployment remains separate. Native SDK workers remain explicitly trusted same-user processes. Windows/Linux and remote production deployment still require qualification. Build and verification instructions are in [the SDK harness guide](../tests/README.md); earlier measurements remain in [the SDK result report (historical) — pre-public record](history.md).

The gateway packages must be updated together: the unreleased wire protocol now uses duplex exchanges. Session lease waits count against the operation timeout. An incomplete exchange is discarded, not returned to the session pool; every exchange rechecks the binding credential. Idle transport sessions remain until client disposal.

See [current performance evidence](../reports/benchmarks/current/README.md) for the current delivered core and optional loopback Gateway measurements.
