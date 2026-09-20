# WeavePort 0.4.0 pre-publication audit

Reviewed 20 September 2026. This records source/package/Docker checks before the separate clean-source and GitHub release qualification. It is not a publication receipt.

## Findings and corrections

1. The previous candidate verifier omitted the newly added reuse suites. The frozen qualification now builds and checks source and packed consumers, verifies Hosting/SDK DLL hashes against the fixed package set, freezes the resulting executables, and exercises all three maintained author examples. Sonar coverage also includes scheduling and reuse suites.
2. The scheduler signalled call completion before releasing active state. Result/error completion now follows the activity/idle update under the scheduler lock. Public post-completion assertions cover the handoff. The verified requirement is archived as `2026-09-20-fix-scheduled-completion-handoff`.
3. Candidate versions and current guides still mixed release status with development labels. Core packages, exact compatibility declarations and consumers now use 0.4.0; Python and TypeScript use 0.2.0. Existing public constructor signatures remain present. Historical reports retain their original versions and hashes.
4. Python/TypeScript SDK distribution was not part of the NuGet release artifact export. Export now requires their original qualified bytes in the frozen manifest. The GitHub release receives the wheel/tarball along with core packages/symbols; no new registry or long-lived credential is introduced. Missing/modified author SDK controls fail before export.

## Runtime audit boundary

Reviewed deployment/version/policy matching, exclusive checkout, cleanup acknowledgement, failure/cancellation retirement, pinned streams, old-context expiry, callback authority, old-binding disposal, explicit restart, idle expiry, quarantine accounting and shared fair admission. Default customer-bound workers do not enter the approved pool. Approval remains operator-owned and applies to the complete deployment, not an untrusted plugin claim.

Registered cleanup is cooperative: it does not erase arbitrary globals, runtime copies, filesystem state or unregistered background work. The intentional hidden-global control remains an expected demonstration of this limit. Native same-user execution is not promoted to hostile-code isolation. Historical throughput is not substituted for final package capacity; cold-start and registration costs remain documented.

## Checks completed before committing the release candidate

- 1,036 source and 1,036 packed multilingual reuse assertions.
- 344 assertions per Docker provider (C#, Python, TypeScript): 1,032 assertion executions.
- 382 existing packaged multilingual SDK regression checks.
- Six public API/package checks and two negative compatibility gates.
- Twelve release tooling tests, including missing/modified author SDK refusal.
- Nine alternating-customer calls through the maintained author examples.
- Core readability/style, maintained documentation links, current/pending public-tree checks and specification validation.

Local logs are retained in the ignored release-audit-040 artifacts directory. Clean-checkout qualification and GitHub CI must pass before merging/tagging/publication. The established `nuget-org` environment approval and Trusted Publishing policy remain in place.

## Static report-format finding

CodeQL flagged the historical report writer’s trusted-mode throughput bounds as possible sensitive clear-text data. The source fields are numeric `qualifiedLow` and `firstRejected` capacity measurements. The formatter now converts inputs explicitly to numbers, rejecting arbitrary strings. Representative retained capacity values preserve identical formatting; measurement records are unchanged. The reproduction-script manifest entry is refreshed for this audited script correction; its previous source remains in Git history. The release waits for the renewed CodeQL result rather than excluding the report tree.

## Final main-branch quality review

The main-branch SonarQube analysis measured 83% coverage of new code and no security hotspots, but rejected seven maintainability findings. The follow-up factors callback handling into a focused method, uses explicit pending-status branches, places cancellation last in the internal scheduled-call constructor, removes invalid exception parameter names, and centralizes the C# session expiry guard. Session metadata reads now also take the cleanup lock. No public contract changes are introduced by these corrections.

The standalone Python wheel and TypeScript tarball now contain the repository MIT license and author-facing README. Release export verifies each embedded license against the repository copy in addition to checking the frozen artifact hashes. A negative control rejects a mismatched license before creating output.
