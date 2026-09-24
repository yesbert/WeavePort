# Documentation audit — 2026-09-24

## Outcome

Audited the current guides, examples, package readmes, SDK documentation, all 20 capability specifications, generated retrieval context and website generation against the checkout's implementation, compatibility inputs and preceding frozen qualification. Corrected current guidance and preserved dated evidence as historical evidence. Runtime/example implementation and package versions were not changed by this audit.

## Findings and corrections

| Area | Finding | Correction |
| --- | --- | --- |
| Release identity | Same development labels concealed APIs added after the published release | Explicit distinction between original v0.7.0 downloads and qualified post-release source in status, compatibility, diagnostics, SDK/gateway guidance, entry points and generated API references |
| LLM coverage | Failure catalogue, optional APIs, security/protocol and contributor conventions were missing | Expanded curated retrieval; both complete API baselines included; same-build Markdown and raw API exports |
| Website navigation | Portable sealing, reuse, scheduling, failure codes, engineering and several executable examples were difficult to discover | Added navigation and local rendered pages, including a dedicated optional API page |
| Exceptions and logging | Cancellation event claims and unknown-code handling contradicted implementation; event 1008 and additional 1003 fields were incomplete | Document actual boundary logging, safe fields, normalization, local synchronous opt-in delivery, legacy fallback and execution uncertainty |
| Protocol | Old Docker/PoC framing omitted current native/shared ownership, reserved fields and additive errors | Reconciled protocol 1/2, SDK operations, cleanup and byte/count limits with implementation and generated contract |
| Ownership | Generic no-reassignment statements contradicted approved reuse and Shared execution | Scope affinity to CustomerBound and distinguish sequential cleanup from concurrent trusted sharing |
| Packages and dependencies | Package table still excluded published optional packages; Hosting parser dependencies were absent from guide inventory | Correct seven-package scope and document current Tomlyn/Chasm/logging identities and retained notices |
| Installation/examples | Historical bundle described as current/self-contained; source example omitted feed preparation; discovery snippet used nested branch alternatives | Clarify framework-dependent historical delivery, current installation route, preparation and early-exit example |
| Formatting | Blanket Black/Prettier commands would rewrite generated protocol modules | Exclude generated modules and document generation/check ownership |
| Historical benchmarks | Experimental scheduler statements read as current production policy | Preserve measurements, date/scope experimental APIs and link implemented ownership policies |
| Specifications | PoC/internal phrasing and unqualified exclusive/Docker scenarios could mislead | Clarify existing scope only; no new behavioral requirement or runtime policy |

## Verification executed

- Full DocFX build with warnings as errors: **0 warnings, 0 errors**; **64 HTML files and 1,073 local references** validated, including anchors, assets, search index, sitemap and same-origin AI retrieval targets.
- Website regression suite: **11 tests passed**, including complete optional API retrieval, same-build source coverage, critical navigation, all documented failure categories and maintained landing-page provider snippets.
- Architecture regression suite: **5 tests passed**; source dependency/facade/central-version/logging-catalogue check passed.
- Protocol fixtures: **5 independent assertions passed**; generated protocol drift check passed.
- Deployment regressions: **2 tests passed** using temporary fixture directories only; no site was deployed.
- Repository/artifact relative-link tests, public-tree positive/negative checks, generated LLM freshness, strict OpenSpec validation and whitespace check passed.
- Actual SDK/target/language checked: **10.0.401 / net10.0 / C# 14.0**.

The 296 non-documentation runtime, SDK, example, protocol and compatibility inputs compared byte-for-byte with qualified source `2f858d909a09759aa0753b168381c209932dd59d` were unchanged. Both API baselines also match that source exactly. The preceding qualification at `artifacts/candidates/20260924-094229-b6d49e62/result.json` passed 3,581 assertions over 66 stages, including packed MCP and multilingual reuse examples, SDK streams/sources, TLS/composition, all three application examples and compatibility. Its durable record is `openspec/changes/archive/2026-09-24-unify-errors-and-protocol-contracts/verification.md`.

That runtime/example evidence is reused for unchanged implementation; it is **not** a new full candidate run for these documentation edits. Updated package readmes would change newly packed artifact bytes. No new package, platform, performance, live-site deployment or authenticated GitHub MCP qualification is claimed. Docker and unrelated services were untouched.

## Inventory and treatment

Inventory includes tracked and non-ignored untracked Markdown/text inputs, excluding dependency/artifact caches and private instruction links. Counts were captured before adding this audit record; generated content is verified from its canonical inputs rather than maintained independently.

| Category | Documents | Treatment |
| --- | ---: | --- |
| Maintained guides and example instructions | 72 | Read for current guidance; cross-check versions, contracts, source paths and example/test entry points. |
| Archived decisions and verification | 121 | Preserve decisions and historical scope; check repository links. |
| Active planning | 6 | Keep unfinished platform work separate from guarantees; validate planning. |
| Current specifications | 20 | Review all capability requirements and qualification scope; strict validation. |
| Reviewed API baselines | 2 | Compare to the qualified snapshot and export both completely. |
| Third-party notices | 7 | Preserve exact retained text; reconcile dependency guide inventory. |
| Generated retrieval documents | 2 | Regenerate and verify deterministically. |
| Historical measured evidence | 38 | Check links and dated provenance classification; retain recorded counts and identities. |
| Fixture and branding inputs | 3 | Preserve sample data and design input, not runtime guidance. |

## Maintained guide inventory

- `CONTRIBUTING.md`
- `README.md`
- `benchmarks/WeavePort.Registry/README.md`
- `benchmarks/WeavePort.Reuse/AUTHOR-GUIDE.md`
- `benchmarks/WeavePort.Reuse/MATRIX.md`
- `benchmarks/WeavePort.Reuse/POLICIES.md`
- `benchmarks/WeavePort.Reuse/README.md`
- `benchmarks/WeavePort.Shared/README.md`
- `benchmarks/WeavePort.Streaming/README.md`
- `docs/ai-documentation.md`
- `docs/architecture.md`
- `docs/benchmarking.md`
- `docs/bulk-composition.md`
- `docs/continuous-integration.md`
- `docs/density-testing.md`
- `docs/dotnet-guidance.md`
- `docs/embedded-coordinator.md`
- `docs/engineering.md`
- `docs/failure-codes.md`
- `docs/fair-scheduling.md`
- `docs/gateway.md`
- `docs/history.md`
- `docs/installed-plugin-clients.md`
- `docs/installed-plugins.md`
- `docs/internal-distribution.md`
- `docs/local-execution.md`
- `docs/mcp-plugins.md`
- `docs/native-operations.md`
- `docs/optional-dependencies.md`
- `docs/package-compatibility.md`
- `docs/package-composition.md`
- `docs/package-gateway-client.md`
- `docs/package-gateway.md`
- `docs/package.md`
- `docs/platform-qualification.md`
- `docs/plugin-sdk.md`
- `docs/portable-installations.md`
- `docs/protocol.md`
- `docs/releases.md`
- `docs/reusable-plugins.md`
- `docs/runtime-diagnostics.md`
- `docs/security-architecture.md`
- `docs/shared-execution.md`
- `docs/soak-testing.md`
- `docs/source-organization.md`
- `docs/status.md`
- `docs/v1-integration-contract.md`
- `docs/worker-lifecycle.md`
- `examples/gateway/README.md`
- `examples/mcp/README.md`
- `examples/reuse/README.md`
- `examples/shared/README.md`
- `examples/sources/README.md`
- `samples/AppointmentDesk/README.md`
- `samples/DecisionRoom/README.md`
- `samples/DocumentWorkshop/README.md`
- `sdks/python/README.md`
- `sdks/typescript/README.md`
- `tests/README.md`
- `tests/WeavePort.ReuseTests/README.md`
- `tests/WeavePort.SdkVersionTests/README.md`
- `tests/WeavePort.Shared.Tests/README.md`
- `tests/mcp/README.md`
- `website/README.md`
- `website/concepts.md`
- `website/faq.md`
- `website/getting-started.md`
- `website/imprint.md`
- `website/index.md`
- `website/introduction.md`
- `website/packages.md`
- `website/privacy.md`

## External reference checks

The [llms.txt proposal](https://llmstxt.org/) confirms the compact linked index and Markdown discovery approach. The [official GitHub MCP remote guide](https://github.com/github/github-mcp-server/blob/main/docs/remote-server.md) still documents repository toolsets and read-only paths. [DocFX Markdown documentation](https://dotnet.github.io/docfx/docs/markdown.html) was consulted alongside the actual pinned build. These checks do not claim that every external historical evidence URL was downloaded or that a signed-in client was exercised.
