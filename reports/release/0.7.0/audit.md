# 0.7.0 release audit

Reviewed 2026-09-23 against the portable-installation and consumer-findings changes. This is a source/contract audit with executable regression evidence, not independent penetration testing or hostile-plugin containment qualification.

## Original consumer findings

| Finding | Resolution | Evidence |
|---|---|---|
| Python-only sealing and private compatibility data | Public .NET sealer uses Hosting's embedded matrix; repository drivers delegate to it | Portable packed consumers, independent manifest fixture, deterministic relocation/pin checks |
| Machine-specific runtime hashes | Schema 2 checks ecosystem requirements; schema 1 and optional strict hashes retain integrity behavior | Python/npm oracle fixtures, actual .NET framework comparisons, macOS-to-Linux unchanged bundle checks |
| Generic artifact version startup failure | Explicit assembly informational-version helper; expected/advertised diagnostics before dispatch | Packed C#/Python/TypeScript version tests, all ownership envelope cases, local/gateway diagnostic tests |
| Discovery silently hides unselected directories | ListAll/ListAllAsync return a verified installation or refusal per directory | Missing selector, malformed manifest, wrong contract, cancellation, root failure and valid sibling checks |
| Typed clients hide invocation duration | Immutable per-call typed/JSON metadata, optional gateway fields and explicit unavailable timing | Concurrent out-of-order local/gateway results, legacy client default implementation, shared executable example |
| Consumer adapter metadata | Remains consumer-owned and included in sealed bundle hashes | Complete inventory/hash verification |
| Uncertain outcomes | MayHaveExecuted remains authoritative; no automatic retries introduced | Pre-dispatch mismatch checks and existing cancellation/recovery suites |
| Shape and contract validation | Remains application-owned, after exact installation contract selection | Existing sample and installation checks |

## Audit corrections

- Normalized malformed installed .NET framework metadata into an installation refusal. Before this correction a missing runtimeOptions property could escape diagnostic enumeration and hide subsequent entries. Sync/async regression covers the failing framework inventory.
- Updated startup-version tests to assert the new distinct status and no dispatch; retained malformed-protocol failures separately.
- Removed obsolete Python sealing prerequisites from C# sample documentation, replaced the shared example's Python wrapper invocation, corrected old author SDK/gateway wording, and documented all new result/error contracts.
- Reviewed additive public API snapshots and protobuf field numbers 7–9. No existing field was renumbered. Older/custom clients return unavailable timing instead of a fabricated measurement; immutable per-call values avoid concurrent LastResult races.
- Reviewed cancellation, disposal, approved runtime selection, bounded probing, bundle inventory/pins, version parsing, cross-tenant metadata exposure and default logging. Default logs still omit exception messages, version strings, payloads and credentials.
- Package versions, exact compatibility policy, examples, fixtures, generated retrieval documents and source lockfiles are updated together. Historical reports and archived changes retain their original identities.

## Verification and limits

A NuGet vulnerability query for the Hosting and Gateway server dependency closures reported no known vulnerable packages on the configured sources on 2026-09-23. This is an advisory snapshot, not proof of absence of vulnerabilities.

Focused source Hosting and Gateway tests, packed multilingual version checks and packed optional HTTPS/composition checks passed before the version bump. Final frozen candidate and release workflow evidence are recorded in the release record after execution.

Native code is trusted. Runtime compatibility does not attest runtime dependencies or prevent deployment files changing after validation. Same bundle execution in a Linux container is narrower than full Linux package/platform qualification. Windows, bare-metal Linux, Native AOT and new capacity/performance guarantees are not added by this release. Python/TypeScript 0.3.0 remain GitHub release assets; no PyPI/npm publication is claimed.
