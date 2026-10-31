## Update workflow
Adapt the NextPA-V2/HiveWeaver-V2 sequence to this repository rather than importing Aspire/Nx application steps. Discover tracked manifests only, exclude historical evidence and fixture data, and keep internal WeavePort identities and local tarballs under deliberate release allocation. Resolve all registry requests before writes. Failed registry requests are errors, not apparent up-to-date results. Resolve npm peer constraints without force/legacy-peer-deps; restore original manifest and lock bytes on failure. Refresh transitive npm resolutions within the selected constraints.

Pins are explicit data with registry, package, version, reason and review condition, optionally scoped to a manifest. Report and repair held-version drift. Do not execute shell snippets from a pin file. The updater currently supports literal stable package versions, exact Python requirements and existing pinned CI tool commands; unsupported version syntax fails for review rather than being guessed. It never accepts dependency/API baselines automatically.

## Compatibility
Tomlyn 2.10.1 replaces the removed Toml.ToModel API with TomlSerializer.Deserialize<TomlTable>. Built-in DOM containers support disabled reflection. Retain static project.requires-python validation, bounded input, dynamic-field rejection and normalized exception behavior. Preserve public APIs and exact signature/manifest conventions. Upgrade all .NET package identities together; changed author SDK artifacts receive new patch versions. Reseal/re-sign independent fixtures for the new exact matrix, retaining only discarded-key test signatures.

## Sources and limits
Reviewed current Microsoft guidance and official upstream documentation before implementation:
- https://learn.microsoft.com/dotnet/core/tools/dotnet-package-list
- https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/nugetaudit-transitive-packages
- https://xoofx.github.io/Tomlyn/docs/migration/
- https://xoofx.github.io/Tomlyn/docs/serialization/

No suppressed vulnerability, analyzer or public API mismatch is implied by this update. Check exact package licenses and downstream closure before accepting baselines. SDK/target/language and qualification identities are recorded in release evidence. Native functional qualification does not establish new containment or performance guarantees.

OpenSpec 1.16.0 promotes overlong requirement summaries to strict-validation failures. Shorten the nine existing summaries while preserving requirements and scenarios; this is editorial compatibility with updated tooling, not a product behavior change.

## Gateway cancellation regression
Two hosted candidate attempts exposed Unavailable while cancelling source collection. gRPC can translate interrupted I/O into an Unavailable read exception even after committing a Cancelled call status. Transport reads/writes now consult the completed call status only when the owned token is requested. A committed Cancelled status yields OperationCanceledException retaining the original cause; unavailable/incomplete status and unrequested tokens preserve the original error. No blanket Unavailable-to-cancellation mapping or automatic replay is introduced. A deterministic 12-case reader/writer fixture fails before the correction and passes afterward. The added specification scenario clarifies the existing cancellation-ownership requirement.

Sources: [Microsoft gRPC error handling](https://learn.microsoft.com/aspnet/core/grpc/error-handling?view=aspnetcore-10.0), [gRPC call cancellation/status](https://github.com/grpc/grpc-dotnet/blob/a7767e28a2c2f4e89a17e9efb95f75e8a82637d0/src/Grpc.Net.Client/Internal/GrpcCall.cs), [gRPC reader exception translation](https://github.com/grpc/grpc-dotnet/blob/a7767e28a2c2f4e89a17e9efb95f75e8a82637d0/src/Grpc.Net.Client/Internal/HttpContentClientStreamReader.cs).
