## 1. Establish implementation evidence

- [x] 1.1 Recheck SDK, target and language versions; read lifecycle and architecture guidance; record current official API sources for probing, parsing and atomic file replacement.
- [x] 1.2 Evaluate exact TOML, Python-specifier and npm-range parser dependencies and licenses; retain dependency and conformance decisions in the design before adoption.
- [x] 1.3 Add independent schema-1/schema-2 fixtures covering portable, strict and external-SDK cases, with explicit expected identities and refusals.

## 2. Implement ecosystem requirements and probing

- [x] 2.1 Implement .NET runtime declaration parsing and matching for the documented stable framework-dependent forms; verify all six roll-forward policies, minimum patches, missing and multiple frameworks against runtime conformance cases.
- [x] 2.2 Implement Python project requirement extraction and specifier matching; verify bounds, exclusions, compatible releases, prerelease handling and missing/dynamic/malformed metadata.
- [x] 2.3 Implement Node engines extraction and npm-compatible range matching; verify comparator sets, disjunctions, caret, tilde, wildcards, prereleases and invalid input.
- [x] 2.4 Implement bounded approved-executable probes with cancellation, output limits and stable diagnostics; test hangs, failures, oversized output and startup/selection environment overrides without executing plugin code.

## 3. Integrate installation resolution and startup

- [x] 3.1 Add schema-2 manifest validation, declaration-source agreement and separate runtime/external-file policies while preserving schema-1 behavior and existing public entry points.
- [x] 3.2 Add cancellable asynchronous catalog operations and bounded synchronous compatibility paths; verify selectors remain unchanged on refusal and unapproved aliases never fall back to PATH.
- [x] 3.3 Carry runtime policy into installed binding and validate before initial and replacement worker starts; prove incompatible runtimes are refused without plugin execution for each supported ownership path.
- [x] 3.4 Verify unchanged portable pins survive relocation while modified manifests, bundles, external SDK files and strict runtime hashes are refused without mutating persisted application state.

## 4. Deliver package-contained sealing

- [x] 4.1 Add the public sealing service and options using embedded compatibility metadata, shared validators and deterministic atomic output; document the API and review the packed surface baseline.
- [x] 4.2 Test offline .NET/Python/Node sealing without installed target runtimes, deterministic manifests across paths, unsafe inputs and preservation of previous manifests on failure.
- [x] 4.3 Introduce a .NET build driver using the public API and migrate first-party sealing callers; preserve explicit external SDK hashes and keep build cleanup outside the sealer.
- [x] 4.4 Prove sealing and subsequent binding from packed NuGet consumers without a source checkout, consumer matrix copy or Python dependency for .NET sealing.

## 5. Qualify portability and document support

- [x] 5.1 Seal one managed-only worker on macOS and execute identical manifest/bundle bytes on Linux, retaining digest and runtime evidence; verify compatible and incompatible runtime cases.
- [x] 5.2 Run a host container with an externally mounted plugin root and no bundled adapter, using the same sealed artifact; retain the successful invocation evidence without disrupting unrelated services.
- [x] 5.3 Qualify Python and Node installed launches with real approved runtimes and record matching, refusal and strict-mode behavior; distinguish fixtures from executed combinations.
- [x] 5.4 Measure runtime-probe startup overhead and document scope, supported syntax/deployment forms, diagnostics, strict mode, external files, offline migration, rollback and exact-pin limitations.
- [ ] 5.5 Run focused installation/lifecycle and packed-consumer checks, code-style verification, OpenSpec strict validation, public-tree/generated-document/link checks, git diff checks and committed-HEAD verification; resolve failures and record evidence before marking implementation complete.
