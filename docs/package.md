# WeavePort internal packages

WeavePort is an embedded backend plugin platform for owner-controlled applications and plugins. This internal API targets .NET 10 and is not a stable public release.

Use `WeavePort.Abstractions` for contracts, `WeavePort.Hosting` for binding and worker ownership, `WeavePort.Sdk` to author C# plugins, and `WeavePort.Sdk.Client` for typed calls and bounded result streams. Applications own domain contracts, callback authorization and durable state. Hosting depends on the MIT-licensed Microsoft logging abstractions; it installs no logger provider or global configuration.

A `PluginHost` owns the shared worker budget. Bind immutable `PluginContext` identity, a trusted execution profile, application callbacks and explicit callback grants with `BindAsync`. Dispose clients, bindings and the host deterministically. Cancellation can leave external effects uncertain: retries require application idempotency or reconciliation. Cleanup exceptions report failure after independent cleanup attempts; callbacks retain admission until they actually complete.

`ProcessProfile` requires explicit trust and absolute executable paths. Native processes provide no filesystem/network sandbox or hard resource ceilings. Docker profiles require an existing engine and prepared images. Installing packages does not install either runtime. Timeouts must be positive and at most 4,294,967,294 milliseconds.

Installed-plugin resolution verifies the exact manifest and artifact hashes against owner-supplied pins, rejects missing compatibility declarations, and separates installed versions from activation for future operations. Preserve old artifacts while retained application state references them. The internal distribution includes copyable examples, an offline package feed, compatibility guidance and a native recovery runbook.

The qualified .NET package set is `0.1.0-internal.2`; host API and wire protocol remain version 1. Python and TypeScript author SDKs remain version `0.1.0`. Do not mix binaries from different deliveries merely because an API or protocol number matches. Exact component hashes and qualified runtimes are recorded in the distribution manifest.

Optional diagnostics use `new PluginHost(logger)` with an application-owned `ILogger<PluginHost>`. Events 1001–1005 identify startup, callback, invocation, cleanup and maintenance failures; event 1006 identifies host admission refusals using fixed reason codes. Events include safe worker/correlation/stage/type fields and exclude payloads, configuration, credentials, raw stderr and exception messages. The existing constructor keeps logging disabled.
