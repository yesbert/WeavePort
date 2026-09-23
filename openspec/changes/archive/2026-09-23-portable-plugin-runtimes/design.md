## Context

See [proposal.md](proposal.md) for motivation and scope. Schema 1 combines runtime executable hashes and external SDK source hashes in `RuntimeFiles`. `InstalledPluginCatalog.Resolve` verifies them and returns local paths; installed binding later constructs the process profile. The Python sealer copies `compatibility/local-v1.json`, whereas the host independently embeds that matrix. Public installation identity hashes the exact manifest bytes.

This design describes the implemented change. Qualification evidence and remaining verification tasks are recorded in the task list and the portable runtime verification report.

## Goals / Non-Goals

**Goals:** Separate runtime compatibility from executable integrity without losing external-file checks; allow offline sealing from a packed .NET package; preserve existing source-level entry points where possible; make runtime refusal actionable.

**Non-Goals:** Reproduce a package manager, restore dependencies, execute build backends, infer application domain metadata, alter worker ownership/isolation, or promise portability for RID-specific native assets, self-contained executables or Native AOT. Existing supported strict installations remain usable.

## Decisions

### 1. Explicit schema 2 with legacy preservation

Schema 2 retains identity, entry points, complete file inventory, compatibility and launch fields. A `Runtimes` map associates each entry-point alias with its ecosystem, relative declaration source and extracted requirement; an optional executable SHA-256 adds strict integrity. A distinct `ExternalFiles` hash map preserves explicitly declared external SDK/code dependencies. Operator-approved mappings supply local executable and external-file paths, never paths selected by manifest text or PATH fallback.

The resolver reparses the hashed declaration source and verifies agreement with the recorded requirement. Runtime sources must be inside the bundle. Every entry alias requires exactly one runtime declaration. External aliases cannot masquerade as executable requirements. Schema 1 keeps the original validation path, including SDK source hashes and its full manifest identity. Unknown schemas and mixed/conflicting policy fields are refused.

Making schema-1 hashes optional was rejected: it would silently weaken old contracts and blur legacy pins. Removing all `RuntimeFiles` validation was rejected because some entries protect SDK code rather than a runtime executable. A new manifest still hashes exact bytes, with no automatic pin conversion.

### 2. Ecosystem declarations, explicit supported forms

- .NET: read the entry assembly's adjacent `.runtimeconfig.json`. Support stable framework-dependent declarations using `framework` or `frameworks`, framework names, minimum versions and the six documented `rollForward` policies, including per-framework settings. Default to Minor only where the .NET rules do. Refuse unsupported legacy selection knobs or prerelease declarations explicitly in the first implementation. Do not infer runtime compatibility from SDK version. Validate against frameworks available through the approved muxer and use framework resolution conformance fixtures, including multiple frameworks.
- Python: parse TOML and read static `[project].requires-python` with Python packaging specifier semantics. Reject a missing or dynamically supplied requirement. Use ecosystem-compliant specifier parsing; do not reinterpret it as SemVer.
- Node: parse the entry package's `package.json` and `engines.node` with npm range semantics, including comparator sets, disjunctions, caret, tilde, wildcards and prerelease rules. Treat this declaration as mandatory for portable WeavePort resolution even though npm itself can treat engine constraints as advisory.

A manifest can carry different requirements for different language entry points. The sealer derives them; consumers do not maintain an alternative WeavePort range syntax. Initial portable .NET support is limited to stable framework-dependent bundles; unsupported deployment forms fail clearly instead of falling back to unchecked execution. Python and Node parser conformance and exact dependency licenses are implementation gates, not grounds for silently narrowing accepted syntax.

### 3. Bounded validation using the approved executable

Probe the exact approved runtime with shell execution disabled, fixed diagnostic arguments, redirected bounded output, a finite timeout and cancellation. .NET probes installed frameworks, Python probes interpreter version without importing plugin modules, and Node probes its version without loading plugin code. Run probes outside bundle directories and neutralize runtime startup injection and selection overrides that could invalidate the observation. Never invoke package managers, lifecycle hooks or plugin entry points for detection.

Extract process launching/probing behind an internal test seam. Reuse one observation within a single validation operation only; do not keep an unbounded global cache of executable paths. Validate during resolution and immediately before each installed worker start, including replacement workers. Bind the approved executable and normalized runtime selection environment to that start. Environment or command overrides must not weaken the declaration; unsupported overrides cause refusal. No extra probe is added to each invocation of a live worker.

Add asynchronous resolution/listing/activation paths where probes require I/O; keep existing synchronous API behavior source-compatible using a bounded synchronous probe path rather than blocking asynchronous tasks. Share pure validation logic. Installed binding/startup uses cancellable asynchronous probing. Failures distinguish malformed declaration, unapproved runtime, probe failure, incompatible runtime and strict hash mismatch, including expected and observed versions without dumping environment variables or payloads.

The existing stable-deployment precondition remains: this is not a TOCTOU defense or a runtime attestation mechanism. The deployment must not mutate runtime files while they execute. Rechecking on worker replacement is not permission to modify a live runtime.

### 4. Public sealing service in the hosting package

Introduce a cohesive `PluginInstallationSealer` API with an options record for release directory, plugin/version/contract, entry points, declaration locations, launch settings and optional strict/external files. Keep authoring separate from catalog discovery. Return the resulting installation identity after an atomic same-directory manifest replacement. Validate metadata and inventory before replacing any existing manifest; use deterministic field ordering and serialization and no timestamps or absolute paths in portable metadata.

Use the same embedded compatibility policy and pure manifest/path validators as resolution. Portable sealing does not probe target runtimes or resolve the finished installation locally. It hashes complete files, including runtime declaration files and consumer metadata. Reject unsafe links and invalid metadata; do not delete bytecode caches or other files as a side effect. First-party build scripts perform any intentional build cleanup before invoking sealing.

A small repository .NET driver calls this public API for existing build workflows. Existing Python entry points can delegate during migration, eliminating their duplicated policy generation; the supported consumer path requires only the packed .NET API. A separately distributed dotnet tool is deferred because the API already removes both consumer dependencies without another package/release surface.

### 5. Verification must distinguish matching logic from actual portability

Unit/scenario fixtures cover ecosystem parsing, .NET roll-forward conformance, malformed declarations, bounded probe failure, hostile output sizes, overrides and deterministic writes. Packed consumers prove the API works without repository assets or Python, preserving independent manifest fixtures so shared serializer mistakes are detectable.

Seal a portable managed-only .NET worker once on macOS, transfer identical bytes to Linux and run it with an approved compatible runtime. Retain manifest and bundle digests on both hosts. Qualify a container host with its plugin root mounted externally and no adapter baked into the image. Add Python and Node real-process acceptance/rejection checks. Fixture-only version comparisons are not cross-platform execution evidence. If a required environment is unavailable, leave that qualification task incomplete and document the missing evidence.

## Risks / Trade-offs

- Runtime probing adds startup I/O → bounded, cancellation-aware probes at resolution/start boundaries; measure added startup cost and avoid per-call probing.
- .NET selection has multi-framework and environment semantics → conformance cases against actual runtime behavior; explicit rejection of unsupported settings; do not claim SDK equality implies compatibility.
- New parsers increase package surface → inspect exact package licenses, transitive dependencies and packed artifacts before choosing them; document the final dependency decision here.
- Compatible runtimes can change application behavior → pins identify bundle and manifest, not identical runtime behavior; strict executable hashes remain opt-in and do not attest shared libraries.
- Native dependencies may remain OS/architecture-specific → scope cross-host qualification to portable bundles and document deployment constraints beside the portability claim.
- Older hosts reject schema 2 → upgrade host first and keep schema-1 releases for rollback; no automatic migration.

## Migration Plan

1. Add schema-2 resolution and sealing while retaining schema-1 validation and recovery tests.
2. Update first-party build drivers to produce portable manifests through the packaged API. Ensure external SDK sources remain hashed or move them into the hashed bundle explicitly.
3. Review the public API baseline and document schema behavior, supported declarations, diagnostics and runtime ownership. Resealing is an offline deployment step that creates a new identity.
4. Qualify packed consumers and the same sealed artifact across macOS/Linux and an external plugin-root container deployment. Update maintained guarantees only after evidence exists.
5. Roll back by selecting a retained legacy release with its compatible host/runtime deployment. Never rewrite previous pins to pretend a newly sealed manifest is the same installation. Version allocation and publication require a separate release operation.

## Reference Sources

Reviewed 2026-09-23; implementation must recheck API availability against the repository SDK.

- [.NET framework selection and roll-forward](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)
- [.NET framework resolution design](https://github.com/dotnet/runtime/blob/main/docs/design/features/framework-version-resolution.md)
- [Python project metadata](https://packaging.python.org/en/latest/specifications/pyproject-toml/)
- [Python version specifiers](https://packaging.python.org/en/latest/specifications/version-specifiers/)
- [Node package engines](https://docs.npmjs.com/cli/v11/configuring-npm/package-json/#engines)
- [npm SemVer grammar and range semantics](https://github.com/npm/node-semver)

## Implementation evidence

SDK 10.0.401, net10.0 and C# 14.0 confirmed on 2026-09-23. Microsoft Learn MCP confirmed the .NET 10 Process.WaitForExitAsync, StreamReader.ReadAsync and File.Move(source, destination, overwrite) APIs. Manifest writes use a temporary file on the destination volume.

Parser dependency review: Tomlyn 0.19.0 (BSD-2-Clause, source commit ee3e3ca5b1f016b0db21ceb74d6c85d28a08ca16) and Chasm.SemanticVersioning 2.8.2 (MIT, commit 1089b9a30f5cc2e7fb486290104f165d8077c6ef) are selected from exact NuGet artifacts. The latter brings Chasm.Formatting 2.4.0 (MIT, commit 9c5bc477fd6061157a56d1e879c6279823cfec4e); the selected modern target has no further dependencies. Required notices are retained under docs/licenses and packaged with Hosting. Tomlyn's TOML 1.0 API meets pyproject metadata needs without adopting the newer serializer surface. Pep440 1.4.0 was inspected but rejected: its artifact does not declare a license and only provides version comparison, not specifiers. Python version/specifier matching is original C# implementation of the packaging specification, verified against Python packaging fixtures, without copying that package's implementation. These findings concern inspected artifacts and notice obligations, not blanket legal clearance.

API sources: https://learn.microsoft.com/dotnet/api/system.diagnostics.process.waitforexitasync?view=net-10.0 and https://learn.microsoft.com/dotnet/api/system.io.file.move?view=net-10.0.

The public API review confirmed only the intended additions: PluginInstallationSealer, PluginSealOptions and ResolveAsync/ListAsync/ActivateAsync. Existing signatures remain unchanged. Source and packed installation/lifecycle checks pass. All three ownership paths reject incompatible runtimes at initial and replacement starts. Python packaging 25.0 and npm semver 7.7.2 provide independent oracle fixtures. The source-authorized dependency license notices are distributed with Hosting.
