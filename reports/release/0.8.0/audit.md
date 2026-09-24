# Release 0.8.0 audit

## Scope

The release preparation changes identities and delivery guidance for already qualified runtime changes. The seven .NET packages use 0.8.0 and both author SDKs use 0.4.0. Host API 2, native protocols 1/2, third-party dependency versions and public API baselines are unchanged from the reviewed main source. Existing failure/cancellation behavior changes from 0.7.0 are documented in the migration guide.

Current examples, package references, compatibility policy and independent fixtures use the new versions. Package readmes, website and generated LLM content are synchronized. Historical reports and audit ledgers retain their original identity. Python/TypeScript SDKs remain release downloads, not registry publications.

## Preparation findings and corrections

- Updating the independent portable manifest changed its content pin. The first clean candidate failed as intended against the stale fixed digest; the independently computed SHA-256 fixture was updated alongside the new exact-version declaration. The failed run is not counted as passing qualification.
- The website generator still linked the retired post-release status heading. Both generated API links now target the 0.8.0 status section.
- The website retrieval regression expected obsolete post-release wording. It now checks the 0.7.0 migration section and 0.8.0 status anchor. All eleven website tests pass; the website build checks 64 HTML files and 1,079 local references with no warnings or errors.

## Evidence

Preparation source `da839baa37021e0cd933fa6a5d9d8541f3c3ef85` passed 3,599 assertion executions across 73 stages, preserving all 442 frozen artifacts. See [preparation qualification](preparation-qualification.json). [PR 44](https://github.com/yesbert/WeavePort/pull/44) passed macOS qualification, Windows/Linux functional checks, documentation/release tooling, coverage and CodeQL before merging at `eb2f031b1a8e30edd452f6064f5d890e3c73d3e6`.

The first unpublished tag attempt identified that merged commit, but its main CI exposed the cancellation race described below. The release workflow was cancelled before publication and the unpublished tag removed; the corrected tag identity is recorded separately. Exact-tag qualification, publication and public artifact checks are recorded separately in the release record. No Docker service configuration, timeout or quality threshold was changed for this release.

## Prepublication cancellation correction

The additional main CI run [36008208261](https://github.com/yesbert/WeavePort/actions/runs/36008208261) failed when a cancellation callback disposed a gRPC call before the next source read. The retained [failure log](prepublication-cancellation-failure.log) shows `ObjectDisposedException` escaping the expected cancellation boundary. PR/local success did not override this finding. Release attempt [36008213589](https://github.com/yesbert/WeavePort/actions/runs/36008213589) was cancelled before publish; no NuGet/GitHub release artifacts were published. The unpublished tag was removed, rather than releasing known faulty source.

Gateway read/write boundaries now convert disposal only when their owned token is actually requested, preserving the token and original cause. Unrelated disposal still propagates unchanged. A deterministic transport fixture failed before the correction and passes after it; real source/stream readers are resumed after cancellation and deadline expiry to verify cancellation and subsequent worker reuse. These checks run against source and packed consumers, and in coverage collection. No timeout or retry was changed. The [initial analysis](preparation-analysis/summary.md) remains evidence of the original preparation revision, not the corrected tag.

The corrected runtime at `af8a0d7` passed a second clean qualification: 3,599 reported assertions, 73 stages, 442 frozen artifacts; additional cancellation regression output is retained in its logs. See [correction qualification](correction-qualification.json). [PR 45](https://github.com/yesbert/WeavePort/pull/45) at `5818dbd469d5214dc4774af963809b8248cbe27c` also passed complete hosted qualification, including Windows/Linux and CodeQL, before merging at `9955bc476c25b9d69d6e3b6df2358d5b34937607`. The final `v0.8.0` tag points to that corrected merge.

A Windows run on the first correction revision recorded the previously observed intermittent TypeScript contract/callback failure. Its [original result](historical-windows-verification.json) contains only an exception type and does not establish the cause. Safe status/phase/elapsed metadata is now retained for failed native fixture calls. The following Windows run passed without changing its deadlines or adding retries. This is not evidence that the historical failure's root cause was established; native functional CI remains distinct from capacity qualification.

The [hosted correction qualification](correction-hosted-qualification.json) records GitHub PR merge revision `ef893d320b8d90f2f04fc9a7b85ffdc544546035`. Its Git tree `fd6753ce3747050bbe8cd02701f9e8f4a0c0da91` exactly matches PR head `5818dbd469d5214dc4774af963809b8248cbe27c`; the record preserves the actual checked-out commit rather than substituting the branch head.
