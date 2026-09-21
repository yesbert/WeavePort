# Cooperative reusable plugin examples

These examples keep customer data local to one call and register cleanup with the SDK. They do not approve themselves for cross-customer execution; the operator selects `WorkerReusePolicy.ApprovedSessions` after reviewing the entire deployment and its dependencies.

- [C#](csharp/Program.cs): registered byte-buffer clearing and owned disposable stream.
- [Python](python/plugin.py): registered bytearray clearing and owned BytesIO.
- [TypeScript](typescript/plugin.mjs): registered Buffer clearing and a call-local cache.

Build C# against the 0.6.0 core package set with `dotnet build examples/reuse/csharp -c Release`. Install the 0.3.0 Python wheel or TypeScript tarball before running those providers. Their `transform` function takes `{ "text": "hello" }` and returns the authenticated host context's tenant, transformed text and UTF-8 byte count. All three speak the regular SDK protocol and can run in a trusted local profile or a matching Docker image.

Clearing one mutable buffer does not erase strings, copies, serialized output, JIT/runtime memory or library caches. Do not store sensitive data outside the session. See [author best practices](../../docs/reusable-plugins.md) for files, background work, streams and review criteria.
