# WeavePort 0.5.0 integrated release audit

Reviewed 20 September 2026 after merging PR #24 with the 0.4.0 runtime. This is an author-performed source and executable audit, not an independent security certification. It is a pre-publication record; the final release receipt identifies the actual tag, workflow and public downloads.

## Finding and correction

A deliberately invalid HTTPS gateway stream batch (`{}` instead of an array) caused `RemotePluginClient` to throw `InvalidOperationException` while checking array length. The new packed regression reproduced this before the correction. The parser now checks the JSON kind before accessing array members and raises `InvalidDataException` for invalid shape/count. Six negative variants cover object, null, string and numeric roots, too many elements and an oversized item. Each also verifies subsequent authenticated discovery on the same client. This is protocol/error-contract robustness; the tests did not demonstrate cross-customer data disclosure. No public API or wire field changes are introduced.

## Scope and evidence

| Area | Review and executed controls |
|---|---|
| Complete delivery scope | Compared all source projects, the seven-package allowlist and open PRs. The four core packages plus Composition, Gateway.Client and Gateway are included; Testing stays internal. |
| Gateway authority | Traced credential registration, immutable tenant discovery, reauthorization on every exchange, foreign composition refusal, revocation and restart. No payload can select executables or grant itself tenant authority. |
| Transport and streams | Reviewed TLS defaults, client-only framework dependencies, bounded leases/messages/batches, abandoned streams, explicit cancellation and lack of automatic replay. Malformed batch tests now cover both rejection and recovery. |
| Composition | Reviewed reference-identity result handles, tenant checks before dispatch, quota reservation under locks, incomplete-write cleanup, conservative failed-delete accounting, producer drain and cancellation error aggregation. |
| Shared workers | Reviewed exact deployment/version/policy matching, exclusive acquisition, explicit SDK cleanup capability/acknowledgement, timeout/cancellation retirement and pinned stream ownership. HTTPS integration checks alternate customers through one cleaned worker, multi-chunk composition, foreign bindings and revocation. |
| Compatibility | Preserved the 0.4.0 scheduling/cleanup API while adding bound-client discovery and optional package surfaces. Exact 0.5.0 core declarations and optional API/dependency baselines are checked through real NuGet consumers. |
| Dependencies and licensing | Inspected optional dependency closures, build-only Grpc.Tools, MIT package metadata/readmes/icons and upstream license notices. NuGet advisory checks cover all seven resolved package closures; absence of reported advisories is not proof of absence of vulnerabilities. |
| Publication integrity | Fresh committed-source qualification freezes packages and consumers; export selects original allowlisted bytes, provenance and symbols plus the licensed Python/TypeScript SDK artifacts. Required environment approval and OIDC Trusted Publishing remain unchanged. |
| Documentation | Current package inventories, installation versions, examples, website and generated LLM guides are aligned to seven packages at 0.5.0. Historical 0.4.0 reports keep their original identities. |

Focused optional qualification passes 140 assertions plus 24 native composition checks and complete HTTP delivery up to 128 MiB with three-way fan-out. Source/packed reuse, all author examples, core compatibility, existing SDK regressions and final clean-checkout CI must also pass on the final revision before tagging. Final workflow records take precedence over earlier local candidates.

## Boundaries

Approved cleanup remains cooperative; hidden globals and unregistered background activity are residual risks. Native processes run with the application's user rights. TLS tests use loopback, a private CA and separate real workers; they do not establish WAN or proxy performance. The application owns host-wide admission, credential distribution/rotation, private composition storage and crash-orphan recovery. No new server-capacity claim is made from functional qualification.
