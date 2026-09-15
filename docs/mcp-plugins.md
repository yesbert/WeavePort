# Optional local MCP plugins

Reuse local MCP tools from your .NET application while keeping native plugins for application-specific contracts. The 0.3.0 package line adds MCP tools alongside the default WeavePort protocol. No extra runtime package dependency is required by Hosting.

A local MCP server is a child process offering functions over stdin/stdout. One process can offer many tools. It needs neither a network listener nor an AI model. Existing native plugins remain native. Consuming MCP servers is separate from exposing WeavePort functions through an external MCP gateway; this implementation only consumes local servers.

[Run the complete C# example](../examples/mcp/README.md) to discover and call the same server using both supported revisions.

## Bind and call

The snippet uses the public `McpMethods` constants from the current source build. They are not in published NuGet 0.3.0; follow the local-package build in the example, or use `"tools/list"` and `"tools/call"` with that release. The wire protocol is unchanged.

Use an absolute runtime executable and server entry-point path selected by the trusted application. Deploy dependencies beforehand: the host does not download servers or run package managers.

```csharp
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Hosting;

await using var host = new PluginHost();
var profile = new ProcessProfile(
    "/absolute/path/to/node",
    ["/absolute/path/to/server.mjs"],
    trustedCode: true,
    timeout: TimeSpan.FromSeconds(5))
{
    Protocol = ProcessProtocol.Mcp20260728
};

var context = new PluginContext("department-a", "document-tools", "1",
    "local-mcp", JsonSerializer.SerializeToElement(new { }));
await using var session = await host.BindAsync(
    context, profile, new NoCallbacks(), []);

InvocationResult discovery = await session.InvokeAsync(
    McpMethods.ListTools, JsonSerializer.SerializeToElement(new { }));
InvocationResult result = await session.InvokeAsync(
    McpMethods.CallTool, JsonSerializer.SerializeToElement(new
    {
        name = "normalize",
        arguments = new { text = " hello   world " }
    }));

if (result.Status != "ok")
    throw new InvalidOperationException($"Host exchange failed: {result.Status}");
if (result.Value.TryGetProperty("isError", out var error) && error.GetBoolean())
    throw new InvalidOperationException("The tool reported an application error.");
Console.WriteLine(result.Value.GetProperty("structuredContent"));

sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token)
        => throw new NotSupportedException("This binding has no callbacks.");
}
```

The [official-SDK fixture](../tests/mcp/server.mjs) implements normalize and echo as ordinary text functions. [Fixture instructions](../tests/mcp/README.md) include an executable interoperability check.

`InvokeAsync` is the low-level host contract. MCP bindings accept only `tools/list` and `tools/call`. The native author SDK and `LocalPluginClient` operations/streaming are a different protocol and are not transparently translated.

## Versions and results

| Selection | Startup | Scope |
| --- | --- | --- |
| `Native` (default) | Native ready frame | Existing WeavePort protocol |
| `Mcp20251125` | initialize, exact revision check, initialized notification | MCP 2025-11-25 tools subset |
| `Mcp20260728` | server/discover, supported revision check | MCP 2026-07-28 tools subset, metadata on every request |

There is no automatic fallback, sibling probe process, or Unix-socket MCP mode. Unknown enum values and MCP/socket combinations are rejected before startup. The configured plugin artifact version remains a host deployment identity. MCP revision and server-reported implementation metadata do not attest the plugin artifact's integrity or establish equality with that version.

Discovery returns one page. If present, pass `nextCursor` as `{ cursor = value }` in a subsequent tools/list call; the application bounds its own pagination. A tools/call payload contains a nonempty `name` and optional object `arguments`. Other parameters, including caller-supplied protocol `_meta`, are rejected.

The complete result object is preserved, including content, structuredContent and isError. Host status `ok` means the MCP exchange completed, not that the tool succeeded. A valid isError result keeps the worker alive. A JSON-RPC error becomes host status `failed`; malformed or unsupported traffic becomes `protocol-error`. These failures remove the worker. No call is automatically replayed. `MayHaveExecuted` remains conservative: an unsuccessful call can already have caused effects.

## Lifecycle and trust

The existing host owns every process launch and reservation. Global/tenant admission, pristine assignment, state retention, idle release, explicit restart and quarantine accounting apply to MCP as to native workers. A used process is never reassigned to another tenant. Multiple bindings can create multiple instances of the same server.

The host sends neither bound tenant/configuration data nor native callback grants implicitly. MCP bindings reject nonempty native callback grants before registration. The application authorizes which server and tools it calls; discovery names, descriptions, annotations and output are untrusted data, not authority. Content is not executed, and resource links are not fetched automatically.

Local execution still requires trusted code. The cleared environment, private cooperative workspace and separate process do not enforce filesystem/network confinement, hard memory ceilings or containment of escaped descendants. See [local execution](local-execution.md) and [worker lifecycle](worker-lifecycle.md). Requiring unavailable OS protection still rejects binding.

Messages reuse the host's 1 MiB frame limit and depth-32 reader; MCP serialization also limits depth to 32. Oversized output is rejected before sending any part of the message. Response IDs and duplicate envelope fields are checked. At most 32 recognized notifications or legacy keepalive requests are handled per exchange; legacy ping requests receive empty acknowledgements, and other server requests are rejected; the total deadline does not reset. stderr is drained without retaining plugin-controlled text.

Cancellation after a complete request write attempts a cancellation notification for at most 100 ms, then existing cleanup terminates the worker. An interrupted partial write does not append a notification. Legacy initialization is not cancelled with a protocol notification. MCP shutdown first closes stdin and allows up to 100 ms for exit, then uses existing forced termination and cleanup accounting. Cancellation does not roll back external actions.

## Supported boundary and evidence

Resources, prompts, sampling, roots, elicitation, tasks, subscriptions, remote HTTP and multi-round-trip host interactions are not implemented. Unsolicited server requests cannot invoke callbacks. Servers requiring these features need a separate integration change. The tools subset has been exercised against the official TypeScript SDK 2.0.0 and protocol/failure fixtures on macOS; other SDKs and operating systems are not qualified by that result.

[Host regressions](../tests/README.md) cover malformed JSON/UTF-8, size/depth limits, duplicate IDs, wrong correlation, notification flooding, authority, admission, failure recovery and state retention. These are bounded protocol/lifecycle checks, not a penetration-test certification or an OS sandbox qualification. [Benchmark guidance](benchmarking.md) distinguishes native regression timing from MCP SDK comparisons.
