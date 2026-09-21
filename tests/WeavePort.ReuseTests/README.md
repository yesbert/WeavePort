# Actual-host approved reuse tests

The executable can act as a C# SDK provider or drive C#, Python and TypeScript providers through `PluginHost` with immediate and queued admission. It verifies bound defaults, approved compatible sharing, scoped files/cache cleanup, expired contexts, host callback identity, pinned streams, failed/hanging cleanup, explicit in-flight cancellation, idle expiry, concurrent admission and malformed cleanup protocol controls. Hidden-global retention is an expected negative control in approved mode.

Source run after building the TypeScript SDK:

```sh
npm ci --prefix sdks/typescript --ignore-scripts
npm run build --prefix sdks/typescript
dotnet run --project tests/WeavePort.ReuseTests -c Release -- \
  "$PWD" "$(command -v python3)" "$(command -v node)"
```

For the package boundary, prepare fresh core NuGet packages, build/install the Python wheel and TypeScript tarball, restore this project with `-p:UsePackedCore=true` into a new isolated NuGet cache, and pass the installed Python executable, `-` (normal installed Python import), and the absolute installed `@weaveport/sdk/dist/index.js` path as arguments four/five. Verify loaded DLLs against the package contents. Never reuse a stale same-version NuGet cache when qualifying an unversioned local candidate.

The Dockerfiles expect a small prepared build context containing `wheel/` (the candidate Python wheel), `sdk/` (the installed TypeScript dist), `csharp/` (the packed test executable output) and both provider scripts. Build one image per Dockerfile. To test an image, append a language label and image tag after the five normal arguments. These Docker tests run the same public-host assertions, with 64 MiB container limits and no plugin network or host mounts.

Tests intentionally induce failed cleanup and timeouts and remove only their own workers. Run serially with benchmarks and preserve source/package/image identities and output logs. No publication is performed.
