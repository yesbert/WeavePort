## Context

The current library already packs locally and has native consumer checks. The frozen release candidate excludes it. See proposal.md for scope.

## Goals / Non-Goals

Publish a small .NET 10 library with request-owned results and both native-session and SDK-client mapping. No persistence, automatic retries, global ranking semantics or untrusted-process isolation.

## Decisions

- Add `IBoundPluginClient.GetTenantAsync` in Sdk.Client; LocalPluginClient returns its immutable session identity. Composition adds an overload using this contract and verifies identity before any plugin call. Depend on Sdk.Client, not Gateway. Caller-supplied tenant strings were rejected because they cannot attest the remote binding.
- Preserve the native `IPluginSession` overload and the bulk-map base64 object contract. SDK authors register a bulk-map operation. Both paths share response validation and bounded transforms.
- Retain failed-delete reservations until scope disposal; aggregate producer and cleanup failures. Disposal attempts independent cancellation, drain, deletion and cancellation-source release, caching the same outcome. Do not claim successful removal when disk cleanup fails.
- Keep existing 4–256 KiB blocks, default 64 KiB, per-object/scope quotas and branch cap. Review boundary, concurrent quota, malformed reply and shutdown evidence using packed consumers.
- Publish 0.4.0 with the coordinated core/gateway family. Add a separate optional API snapshot; do not add optional packages to mandatory installation declarations.
- Freeze packages once; all consumers and exports use those exact bytes. Include package-specific README, MIT license, icon, source provenance and symbols.

## Risks / Trade-offs

- Disk failure leaves private orphan files → report failure and retain conservative accounting; application manages failed storage and crash remnants.
- SDK dependency broadens Composition's closure → keeps one typed local/remote contract without another adapter package.
- Native filesystem isolation remains cooperative → document trusted host/storage assumptions.

## Migration Plan

Existing native calls remain valid. SDK clients use the new overload and authenticated identity discovery. Qualify package consumers and API snapshots before release export; publication follows the existing tag/environment workflow.

## API references

Reviewed SDK 10.0.401, net10.0, C# 14.0. Microsoft: https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelasync?view=net-10.0 and https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes.

## Implementation evidence

The source implements the planned package split, SDK binding identity and composition integration. The dedicated packed suite passes on macOS with real C# SDK workers, private-CA HTTP/2 TLS, invalid trust/hostname refusal, incompatible discovery version refusal, cancellation, stream abandonment, active revocation, restart and quota/cleanup faults. The existing native C#/Python/TypeScript composition fixture reports 24 checks; HTTP delivery checks include 128 MiB inputs and three-way fan-out. Client-only runtime metadata excludes ASP.NET Core. Frozen clean-candidate qualification is tracked separately in tasks.md.

The first clean candidate at source `ea5e57d` passed 1,188 recorded assertions and retained 215 frozen files. The separate existing multilingual local/gateway SDK suite passed 382 checks. Optional TLS/composition checks report 89 assertions, native composition reports 24 checks, and HTTP delivery covers inputs through 128 MiB. These are functional checks on macOS; no new WAN performance or Windows/Linux qualification is claimed. Final review also corrects the moved protocol path in the soak fixture and local package cache handling. NuGet publication remains a separate release operation.
