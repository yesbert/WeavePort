# Release 0.8.0

## Publication status

Preparation in progress; no 0.8.0 publication is claimed by this record yet. The approved delivery comprises seven .NET packages at 0.8.0, sibling symbols, Python SDK 0.4.0 and TypeScript SDK 0.4.0. Testing remains internal. The author SDKs are GitHub assets, not PyPI/npm publications.

## Scope and migration

See the [0.8.0 migration guide](../../../docs/releases.md#080-failure-contracts-and-code-quality) for new structured diagnostics, error-code normalization, cancellation catch behavior, cleanup-cause preservation and exact installation resealing. Existing .NET signatures and native host/protocol numbers remain; changed failure behavior requires consumer review.

The [readability audit](../../../openspec/changes/archive/2026-09-24-audit-file-readability/verification.md) and [analysis follow-up](../../../openspec/changes/archive/2026-09-24-complete-readability-analysis/verification.md) retain development evidence. They do not replace exact-version/tag qualification. Original reports and per-file audit hashes remain historical snapshots.

## Verification and limits

The release must pass clean candidate qualification, source and packed consumers, API/compatibility and generated-documentation checks before publication. Exact-tag artifact identity and public downloads will be recorded here after the gated workflow. Existing native platform and Docker/performance qualification limits remain; this release does not add new capacity guarantees or automatic application-pin migration.
