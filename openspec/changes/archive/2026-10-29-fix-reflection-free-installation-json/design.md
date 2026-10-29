## Context

The failure is reproducible with a valid runtimeconfig and `JsonSerializerIsReflectionEnabledByDefault=false`. The first failure occurs in DotnetRequirements.Read; compatibility loading and manifest serialization would fail next. Resolution also deserializes framework requirements without explicit metadata.

## Goals / Non-Goals

Preserve schema-1/schema-2 reader behavior and byte-identical schema-2 output while removing implicit JSON reflection from installation operations. This does not qualify all Hosting features or third-party dependencies for Native AOT.

## Decisions

Use one internal metadata-mode JsonSerializerContext for the manifest, compatibility declaration and framework requirement array. Preserve manifest serializer options including string enum conversion, indentation and null omission. Use typed generated metadata for framework requirements and the embedded matrix. Avoid globally enabling reflection or adding a reflection fallback: both would defeat the consumer's deployment choice.

The dedicated executable disables reflection in its runtime configuration, checks that it really is disabled, and exercises sealing, deterministic identity, synchronous/asynchronous pinned resolution and failure preservation. Existing hosting tests compare generated manifest and requirement bytes against the previous reflection-based serializer and retain independently pinned fixture coverage.

Framework check: SDK 10.0.403 selected by global.json roll-forward; net10.0, C# 14. Microsoft guidance confirms generated metadata and consistent reflection disabling on CoreCLR and Native AOT: https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation .

## Risks / Trade-offs

A missing type in the generated graph would fail only on its affected path; cover .NET, Python and Node sealing plus manifest resolution. Generated metadata must preserve property order and options because installation pins hash exact bytes. Native AOT publication remains outside this narrowly demonstrated fix.

## Verification

Before the fix, `dotnet run --project tests/WeavePort.Sealing.Tests -c Release` exited 134 with InvalidDataException for worker.runtimeconfig.json and an inner reflection-disabled InvalidOperationException at DotnetRequirements.Read. After the fix, the same regression passes. Additional suite and package evidence is recorded in verification.md.
