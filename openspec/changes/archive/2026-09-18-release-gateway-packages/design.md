## Context

The current optional assembly contains server, remote client and generated protocol types. Only source regressions run inside the core release candidate. See proposal.md.

## Goals / Non-Goals

Offer separate client/server installation, binding-safe composition, and a documented direct Kestrel HTTP/2 TLS topology. No arbitrary reverse-proxy certification, automatic retry, credential provisioning service, mTLS mandate or remote executable registration.

## Decisions

- Keep `WeavePort.Sdk.Gateway` for hosting; move RemotePluginClient, session pool and shared generated protocol to `WeavePort.Sdk.Gateway.Client`. Preserve namespaces. Client has no ASP.NET Core framework reference. Server references client and Grpc.AspNetCore.Server. Generate protocol once; public generated types are deliberately included in API review.
- Add immutable tenant metadata to registry registration and a credential-authorized describe exchange with gateway protocol version 1. Local client registration infers its tenant; custom clients require explicit trusted tenant input. Remote GetTenantAsync validates protocol and returns server-bound identity without invoking plugins. Never infer authority from payloads or arbitrary caller labels.
- Preserve existing constructors, call/stream bounds and no-retry semantics. Add a transport-options overload using GrpcChannelOptions for private CA/client configuration with caller-owned handler semantics. Library enforces 1 MiB transport bounds and never disables certificate validation by default.
- Add server registration/mapping extensions that install the documented 1 MiB message limits. Application owns HTTPS listener, credentials, authentication and deployment access.
- Test direct HTTPS with an ephemeral private CA, valid/wrong-name/untrusted certificates, two tenant bindings, cancellation, revocation, stream abandonment, disconnect/restart and composition. A local TLS test does not prove two-machine deployment: document exact executed topology and retain a runnable separate-host example for deployment verification.
- Freeze/qualify the three optional packages with the 0.4.0 core family, exact dependency closure, generated/public API snapshot, license evidence and package-specific README. Grpc.Tools stays build-only.

## Risks / Trade-offs

- Protocol types become a reviewed public surface → retain field numbers and additive describe operation; explicitly qualify same-version endpoints and reject incompatible describe versions.
- Trusted host may register incorrect identity for custom clients → document registration as authority boundary; built-in local registration derives immutable identity.
- A client transport pool limit is not a server-global admission budget → require host/application admission and transport ingress limits; do not promise global isolation.
- Two-machine validation requires a second environment → distinguish reproducible TLS evidence from deployment-site verification.

## Migration Plan

Source consumers keep namespaces, move client-only references to the Client package and supply trusted identity for custom registrations. Rebuild both endpoints on 0.4.0. Update exact core installation declarations independently of optional package references. Release through reviewed CI and existing NuGet environment gate.

## API references

SDK 10.0.401, net10.0, C# 14.0. Microsoft: https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0, https://learn.microsoft.com/en-us/aspnet/core/grpc/security?view=aspnetcore-10.0 and https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.ssloptions?view=net-10.0.

## Implementation evidence

The source implements the planned package split, SDK binding identity and composition integration. The dedicated packed suite passes on macOS with real C# SDK workers, private-CA HTTP/2 TLS, invalid trust/hostname refusal, incompatible discovery version refusal, cancellation, stream abandonment, active revocation, restart and quota/cleanup faults. The existing native C#/Python/TypeScript composition fixture reports 24 checks; HTTP delivery checks include 128 MiB inputs and three-way fan-out. Client-only runtime metadata excludes ASP.NET Core. Frozen clean-candidate qualification is tracked separately in tasks.md.

The first clean candidate at source `ea5e57d` passed 1,188 recorded assertions and retained 215 frozen files. The separate existing multilingual local/gateway SDK suite passed 382 checks. Optional TLS/composition checks report 89 assertions, native composition reports 24 checks, and HTTP delivery covers inputs through 128 MiB. These are functional checks on macOS; no new WAN performance or Windows/Linux qualification is claimed. Final review also corrects the moved protocol path in the soak fixture and local package cache handling. NuGet publication remains a separate release operation.
