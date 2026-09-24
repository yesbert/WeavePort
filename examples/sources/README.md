# Plugin sources and live progress

Prepare the matching local package feed with `./scripts/prepare-core-packages.sh`, then run `dotnet run --project examples/sources -c Release` from the repository root.
The example starts a trusted local C# plugin, collects a 200 MiB generated source
into an atomic request-owned result, delivers it to a bounded sink, and prints
three progress items as they arrive. No Docker service is used.

Authors register `Source<TInput>` and return a readable `Stream`; the SDK owns
its disposal. Consumers call `Composition.CollectAsync` with a tenant-bound
client and a `ResultScope`. Collection enforces the scope's byte/object quotas
and removes partial files on failure or cancellation. Use `CopyToAsync` to send
the committed result to an HTTP response or another caller-owned stream.
The same consumer calls work with `RemotePluginClient` through an authenticated
gateway binding. Sources require exclusive bindings; shared workers support
unary calls only.

JSON streams retain the 64 MiB total limit. Large binary sources use bounded
blocks and the result scope's independent quotas. `PluginStreamOptions` controls
one exchange timeout and the total operation lifetime, including consumer pauses.
