# Local package and contract compatibility

Keep the host, SDKs and plugin artifacts on a known-compatible combination. This guide defines the exact identities checked during installation and startup.

**Source candidate package policy for 0.4.0, reviewed 2026-09-18.** The [machine-readable matrix](../compatibility/local-v1.json) defines one exact combination. It is embedded in `WeavePort.Hosting` and consumed by the offline installation sealer. The public NuGet package set uses this exact matrix; no general SemVer range is accepted.

## Separate compatibility identities

| Identity | Current value | Checked by |
|---|---|---|
| Local host API level | `1` | Installation compatibility declaration against the embedded matrix |
| Transport protocol | `1` | Compatibility declaration and existing worker startup protocol checks |
| Core host packages | Abstractions, Hosting, Sdk.Client, each `0.4.0` | Exact declaration plus actual packed/loaded metadata checks |
| C# author SDK (`dotnet`) | `WeavePort.Sdk` `0.4.0` | Entry-specific declaration, packed metadata and native startup/call checks |
| Python author SDK (`python`) | `weaveport-sdk` `0.1.0` | Entry-specific declaration, wheel/installed metadata and native startup/call checks |
| TypeScript author SDK (`node`) | `@weaveport/sdk` `0.1.0` | Entry-specific declaration, npm/installed metadata and native startup/call checks |
| Plugin artifact release | Exact chosen installation, e.g. `1` or `2` | Manifest identity/content pin and worker's advertised release |
| Application domain contract | Exact host-requested ID | Resolver equality, followed by application-owned payload validation |

The example contract IDs are `decision-room/v1`, `document-workshop/v1` and `appointment-desk/v1`. Release 2 of a plugin can implement the same v1 domain contract with the same SDK. None of these versions should be inferred from another. The resolver does not implement domain-schema migration or compatibility ranges.

## Mandatory installation declaration

Every `installation.json` now contains `Compatibility` with `HostApi`, `Protocol`, `HostPackages` and `AuthorSdks`. `HostPackages` must exactly equal the three core host package IDs/versions above. Each entry alias requires its corresponding SDK package/version under `AuthorSdks`; absent, extra, null or unknown SDK entries are rejected. No implicit default is substituted for a missing declaration.

The sealer generates this block from the reviewed matrix; plugins do not choose runtime authority through it. Resolution and activation validate it before returning an installation. Domain-contract mismatch is a separate refusal. A refused activation keeps its prior selector; failed recovery leaves application state unchanged.

The metadata is a trusted deployment declaration, not runtime package attestation. The gate inspects real packed package metadata and loaded assembly versions; the installation resolver validates declarations and file hashes. It does not interrogate every transitive runtime dependency inside a running worker. Same development version labels across different development builds do not prove identical bytes or behavior. Keep one coherent tested deployment and its pinned content; stable-file requirements in [installed-plugin resolution](installed-plugins.md) still apply.

Old manifests without `Compatibility` fail closed. Rebuild/reseal only offline. This changes manifest identity, so existing pins require the original deployment or new application state; recovery never upgrades a pin automatically. Manifest schema is 1 for the first public release. Future format evolution requires an explicit migration/format decision.

## Reviewed package/API surface

The [API baseline](../compatibility/public-api.txt) records exported types and public/protected signatures across the four core .NET packages. It includes parameter names and optional defaults, inheritance/interfaces and enum values. The [packed consumer](../tests/compatibility/Program.cs) detects drift without rewriting the baseline. All four packages target `net10.0` in this candidate.

| Package | Consumer surface |
|---|---|
| Abstractions | `PluginContext`, `InvocationResult`, `HostCall`, `IPluginSession`, `IHostCallbacks` |
| Hosting | `PluginHost`, execution profiles/protection, worker budget/snapshot, installed catalog/result/identity and transport profile types |
| Sdk.Client | `IPluginClient`, typed extensions, `LocalPluginClient`, `PluginCallException` |
| Sdk | `PluginApplication`, `PluginCallContext` |

The snapshot includes Docker/socket profile signatures because they are exported by Hosting; that does not qualify their deployment here. The tested product baseline remains native local macOS, .NET 10, with actual C#/Python/TypeScript SDK calls. Composition and Gateway server/client have a separate [reviewed optional API](../compatibility/optional-api.txt), [dependency matrix](../compatibility/optional-dependencies.json) and packed consumer gate. They are selected independently and do not become mandatory installation declaration entries. Testing remains internal. See [gateway topology limits](gateway.md); these tests do not widen the native worker platform matrix.

A signature snapshot does not prove behavioral, binary or nullable-annotation compatibility. The current snapshot does not encode every custom modifier/attribute; code review and package-consuming runtime scenarios remain necessary. For Python/TypeScript, this milestone records package identity and tests the author/startup contract, not a complete language-level exported-symbol snapshot.

## Change and release rules

1. Classify a change by source, binary, behavior, wire and domain impact. Review signatures, parameter names/defaults, serialization, failure interpretation and recovery data independently.
2. For a supported surface change, inspect the generated `artifacts/compatibility/api-actual.txt`, update the baseline explicitly in the scoped change and add meaningful consumer evidence. The verifier never accepts the new surface automatically.
3. Changing package/API/protocol support requires a reviewed matrix and supported/unsupported combination tests. Do not add a range because version numbers look compatible.
4. Changing domain semantics/schema requires a new application contract identity or explicit compatibility/migration evidence. Changing artifact behavior alone may preserve the contract; active operations still retain their exact artifact identity.
5. Keep package publication, final version allocation and a clean-environment release candidate as separate release actions. The existing development version is not a stable compatibility promise.

The distinction between source, behavior and binary changes follows [Microsoft's library guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes).

## Verify

From the repository root:

```sh
./scripts/decision-room.sh --build --verify
./scripts/document-workshop.sh --build --verify
./scripts/appointment-desk.sh --build --verify
./scripts/sdk-versions.sh
./scripts/verify-compatibility.sh
```

The final script checks actual NuGet identity/dependency closure/target libraries, installed and packed author SDK metadata, loaded versions, embedded policy, the API baseline and installation compatibility/refusal cases. Negative checks use copies to prove that real package dependency drift and an API mismatch fail. Separate sample processes verify state preservation. Historical internal-candidate evidence is in the [retained report](../reports/release/0.1.0-internal.2/candidate/report.md).
