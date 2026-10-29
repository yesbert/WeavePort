# Publisher release verification — 2026-10-29

## Qualified source

Commit `d158d23e559819c621c56c4d8bef68990012660c` passed the normal committed-candidate workflow, `./scripts/verify.sh`, with an installed Python 3.12 directory selected in PATH. The preceding JSON-reflection fix is separate commit `3608f98`.

Local retained evidence is `artifacts/candidates/20261029-210058-85f7af07/result.json` with per-stage logs, fixed package hashes and the frozen artifact inventory. Result: **passed, 73 stages, 3,599 assertions, 442 frozen files**. Artifact inventory SHA-256: `75eb033c62b862c994d2b0caa7b2455c7413cd662e31758c2960836e520309d1`.

Environment: macOS 27.0.1 arm64, .NET SDK 10.0.403, .NET 10.0.14 runtime, Python 3.12.16, Node v26.12.1 and npm 11.20.0. The workflow records executable hashes and all installed shared runtimes. It ran in a fresh detached checkout with isolated dependency caches. Three permitted consumer lockfile refreshes are recorded in result.json. Docker was not used, stopped or reconfigured.

## Feature evidence

- Reflection-disabled source and freshly packed consumers verified offline signing/verification, .NET catalog resolution, complete bundle integrity, immutable copied public keys/policies and exact manifest pins.
- Independent OpenSSL 4.1.0 fixture verification fixes the signed bytes, canonical public-key fingerprint and RSA-PSS/SHA-256/MGF1/SaltLen=32 interoperability. Only the public key and signature are retained.
- Negative cases covered missing, unknown, malformed, duplicate-field, oversized, linked and unsupported signatures; altered manifest bytes, altered files and undeclared content; reserved root versus ordinary nested signature filenames; unsigned/schema-1 compatibility and required-policy refusal.
- Rotation preserved pins, withdrawn keys failed new pinned resolution, independent catalogs retained their own admission decisions, and failed activation preserved selectors.
- A real signed .NET worker remained usable after replacing a host trust snapshot, verifying the documented requirement for explicit disposal/re-resolution when immediate revocation is needed.
- The compiled pipeline/staging example refused an unsigned upload before promotion and resolved the signed, atomically promoted release with the original pin.
- Packed API verification passed against the reviewed additive baseline. No runtime dependency, native protocol or package-version change was introduced.

## Broader regression checks

The candidate passed source/packed hosting, scheduler, SDK, reuse, shared/concurrent execution, MCP interoperability, portable installation, three reference application, optional gateway/composition, recovery and package-compatibility stages. It also passed code style/control-flow, architecture, public-tree, website and documentation checks. The changed-package negative control rejected a mutated package; the frozen artifact comparison passed.

After qualification, only evidence/task completion, specification synchronization, archiving and generated documentation were changed. Strict OpenSpec, documentation, public-tree and diff checks cover that finalization; the implementation bytes remain those of the qualified commit.

## Limits

This qualifies the documented native macOS surface. It does not establish new Windows/Linux execution support, general Hosting Native AOT support, hostile-code containment, rollback prevention or automatic worker revocation. Installation operations run with JSON reflection disabled; the existing author/lifecycle subprocess uses its reflection-enabled runtime path. No packages were published by this change.

## Follow-up code review — 2026-10-29

A second code review found a false-positive risk in the activation-refusal test: the selector already named the requested version, so a premature write could escape detection. The test now starts from a different version and compares exact selector bytes independently after synchronous and asynchronous refusal. No production code changed.

The strengthened publisher suite and focused C# style gate pass. Two temporary negative controls injected selector writes before signature validation, separately in sync and async activation; each failed at its corresponding preservation assertion. Both mutations were reverted and the publisher suite passed again. Reflection-free sealing tests, strict OpenSpec validation and diff checks also passed during this review. This is a test-only follow-up to the qualified implementation above, not an independent third-party approval.
