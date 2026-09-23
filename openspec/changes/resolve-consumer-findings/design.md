## Context and decisions
The original consumer findings distinguish three remaining API gaps from the already completed portable runtime/sealing change. Keep the author default `1` compatible. Add an opt-in helper reading AssemblyInformationalVersionAttribute from the entry or explicitly supplied assembly; remove the `+` build metadata suffix by default, retaining prerelease suffixes. Do not guess from the four-part assembly binding version when informational metadata is missing.

Expose expected and advertised versions as structured diagnostics on invocation failures and client exceptions, not raw default logs. Shared startup exposes the same details through a dedicated exception. No domain function may dispatch after mismatch.

Add ListAll/ListAllAsync with an optional expected contract. Each immediate plugin directory produces either a verified selected installation or a bounded-category refusal and actionable diagnostic. Cancellation and root enumeration failures propagate; one invalid directory must not conceal siblings. Existing List remains unchanged.

Add immutable PluginCallResult<T> with nullable host ElapsedMs. A default interface method allows existing/custom clients to report unavailable timing explicitly. Local clients retain the actual InvocationResult timing, and additive optional protobuf fields preserve it across the gateway. Never substitute round-trip latency or mutable LastResult. Existing CallAsync remains value-only and retains failure semantics.

## Alternatives and limits
Changing the default artifact version would break existing launch profiles. A client LastResult races concurrent calls. Returning zero for unavailable timing fabricates a measurement. Diagnostic discovery does not install or activate code and does not weaken validation. Exception version values are protocol metadata available only to the authorized caller, not logged by default.

## Technical references
SDK checked: 10.0.401, net10.0/C#14. Microsoft documentation confirms informational versions include Source Link commit metadata since .NET 8: https://learn.microsoft.com/dotnet/api/system.reflection.assemblyinformationalversionattribute?view=net-10.0 . Existing lifecycle, architecture and memory guidance apply unchanged.

## Verification
Focused startup, discovery, metadata concurrency and gateway regressions; complete packed candidate qualification; documentation/link/generated/API checks; audit and release artifact verification. Results will be recorded after execution.
