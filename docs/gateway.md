# Gateway packages and HTTPS deployment

The 0.5.0 release adds `WeavePort.Sdk.Gateway` (server) and `WeavePort.Sdk.Gateway.Client` (remote client). The client package targets .NET 10 without requiring the ASP.NET Core shared framework. The server requires ASP.NET Core. Both preserve the `WeavePort.Sdk.Gateway` namespace. See the [runnable example](../examples/gateway/README.md), [client package](package-gateway-client.md) and [server package](package-gateway.md).

## Register and call

Configure a direct Kestrel HTTPS endpoint with HTTP/2 and an appropriate server certificate. Register services with `builder.Services.AddWeavePortGateway()` and map with `app.MapWeavePortGateway()`. The service-specific send/receive limits are 1 MiB. The application owns its listener, authentication, network exposure and infrastructure limits.

Resolve `GatewayRegistry` from the application service provider. `Register(localClient)` derives the immutable tenant from `LocalPluginClient`; `Register(customClient, authenticatedTenant)` requires trusted host-supplied identity. Successful registration transfers client ownership to the registry. Do not register the same owned client multiple times. Revocation disposes the binding and cancels its outstanding stream. Application shutdown disposes every registration, including when another cleanup fails.

Distribute the returned bearer credential through an application-controlled secret channel. It authorizes that binding only. Credentials are never accepted from plugin payloads and cannot remotely register paths, grants or executables. Credentials have no automatic expiration: the application owns provisioning, rotation, revocation and audit. A restarted in-memory registry invalidates old credentials; provision a new binding instead of replaying old credentials.

```csharp
await using var client = new RemotePluginClient(endpoint, credential);
string boundTenant = await client.GetTenantAsync();
// Compare boundTenant with the independently authenticated application request.
var result = await client.CallAsync<MyInput, MyResult>("operation", input, cancellationToken);
```

`GetTenantAsync` authenticates a describe exchange and checks gateway protocol version 1 without invoking plugins. Use coherent 0.7.0 endpoints; arbitrary version combinations are not qualified. Unary `CallWithMetadataAsync` preserves optional host elapsed timing, including `null` when an older server or custom client cannot provide it. Artifact mismatch fields also reach authorized remote callers. These additive fields do not change native worker protocol numbers. Generated public protocol types and field numbers are included in the optional API baseline. The additive describe operation does not change native worker protocol version 1.

## TLS and transport ownership

Remote addresses require HTTPS; plain HTTP is accepted only for loopback development. Default certificate chain and hostname validation remain enabled. For private CA or client transport configuration, the constructor accepting `GrpcChannelOptions` supports an application-configured handler. Handler ownership follows `DisposeHttpClient`; set it to true when transferring a dedicated handler, or keep it false and dispose a shared handler after all clients. The library enforces 1 MiB transport limits. Never use a callback that accepts arbitrary certificates.

The persistent channel leases up to eight duplex sessions per remote client. Unary calls have a default 30-second timeout including queue time. Streams and sources use `PluginStreamOptions` with a 30-second exchange timeout and five-minute total timeout by default; cleanup remains bounded. An exclusive binding permits one active result stream or binary source. These limits do not impose a server-wide connection limit: configure application/host admission and Kestrel/ingress limits for the deployment. Large streams use bounded batches (16 items, 256 KiB); items are limited to 128 KiB and total JSON item bytes to 64 MiB. Unary JSON input/output limits are 512 KiB.

No operation is automatically replayed by the default client. A network failure, deadline or cancellation can leave effects uncertain; inspect `PluginCallException.MayHaveExecuted` and use application idempotency/reconciliation. A subsequent explicit call can establish a fresh transport. Dispose clients and enumerate/dispose streams deterministically.

## Composition

`RemotePluginClient` and `LocalPluginClient` implement `IBoundPluginClient`. Composition's SDK overload checks the bound tenant before invoking `bulk-map`, with the same request-owned storage and byte limits as native mapping. Register a `bulk-map` SDK function accepting and returning a JSON object with base64 `data`; see the [example worker](../examples/gateway/Worker/Program.cs). Composition does not own or dispose the supplied client.

## Qualification boundary

`python3 scripts/verify-optional.py` builds and checks packed consumers, including direct HTTP/2 TLS, private CA validation, certificate hostname/trust rejection, real C# SDK workers, complete composition, stream abandonment, caller cancellation, active revocation, server restart and cross-tenant refusal. The client-only sample's runtime configuration is checked for absence of ASP.NET Core. The frozen candidate includes these exact consumer binaries and package hashes.

The automated TLS topology runs on one machine with separate plugin worker processes. It is not a two-machine performance/availability measurement. The example can run the server and client on separate machines with a trusted DNS certificate; validate firewall, DNS, certificate provisioning and connection lifetime at the deployment site. Reverse proxies, load balancers, WAN performance, arbitrary cloud hosting and hostile native plugins are not qualified by these tests. Platform execution evidence must be recorded separately; .NET portability alone is not Windows/Linux execution qualification.

Bound remote clients also expose `SourceAsync`. The gateway reads the existing authorized local client source and carries bounded binary blocks over gRPC. `Composition.CollectAsync` checks the authenticated binding tenant before committing a scoped result. Shared client views retain unary-only behavior through the gateway.
