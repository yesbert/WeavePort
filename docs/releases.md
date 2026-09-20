# GitHub and NuGet releases

The GitHub delivery target is [yesbert/WeavePort](https://github.com/yesbert/WeavePort). CI verifies documentation and release tooling, and runs the existing frozen-candidate qualification on a standard macOS ARM runner. This is functional qualification, not a hosted performance benchmark or Windows support claim.

## Packages and version

The release allowlist in `build/release-packages.json` contains the four core packages plus `WeavePort.Composition`, `WeavePort.Sdk.Gateway.Client` and `WeavePort.Sdk.Gateway`. Testing remains internal. The three optional packages have a separate API baseline and packed-consumer qualification. Python and TypeScript author SDK publication is separate from NuGet delivery.

The release version is `0.5.0`, licensed under [MIT](../LICENSE). All seven .NET packages and the exact compatibility matrix use 0.5.0; host API and wire protocol remain 1. Python/TypeScript SDK versions are 0.2.0; the qualified wheel and tarball accompany the GitHub release without claiming PyPI/npm registry publication. Historical internal distribution and measurement evidence retain their original identities. Public releases require a complete clean candidate qualification; a tag override cannot substitute for updating compatibility inputs.

The [0.4.0 release report](../reports/release/0.4.0/README.md) retains the exact qualification, artifact hashes and publication verification.

## Trusted Publishing setup

The workflow separates three responsibilities: verification without publication credentials, a gated publish job and a separate release announcement.

Configure these values:

| Location | Setting |
|---|---|
| GitHub Actions repository variable | `NUGET_USER=Yesbert` |
| GitHub environment | `nuget-org` |
| Environment required reviewer | `yesbert` |
| Environment deployment restriction | Tags matching `v*` |
| NuGet.org policy owner | The account owning the WeavePort packages |
| NuGet.org GitHub repository owner | `yesbert` |
| NuGet.org repository | `WeavePort` |
| NuGet.org workflow filename | `release.yml` |
| NuGet.org environment | `nuget-org` |

Create the matching policy in the authenticated NuGet.org account. Scope it to the intended WeavePort packages and permit new package IDs when creating the first release. A policy for Stratara does not authorize WeavePort. Follow [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) for current account requirements and policy activation rules. There is no long-lived API-key fallback.

## Release procedure

1. Choose and review the public version and license. Update exact compatibility inputs and regenerate LLM documentation. Merge the reviewed change with green CI.
2. Create and push an annotated `v<version>` tag at that reviewed commit. The tag must exactly match `Directory.Build.props`; internal suffixes and malformed versions are rejected.
3. The release workflow repeats clean qualification. It checks the qualified commit, package hashes, metadata, readme, source provenance and symbols, then uploads only the original tested allowlisted artifacts.
4. Review the `nuget-org` deployment. After approval, `NuGet/login` exchanges GitHub OIDC identity for a short-lived key and publishes the packages and symbols.
5. Only after successful publication does the announce job create the GitHub release; prereleases are marked accordingly. An existing release entry is preserved on rerun.

Branch pushes and pull requests never publish packages. Publication cannot be undone by deleting a Git tag: NuGet versions remain allocated. A partial publish is resumed with the same tested artifacts and `--skip-duplicate`; investigate any mismatch before rerunning. Never move a released tag to different source.

CI retains qualification logs and manifests for 14 days. Preserve release evidence durably when qualifying a public release. GitHub-hosted runner SDK/runtime identities are recorded by the candidate harness. Docker Desktop on a developer machine is not touched by these workflows.

## 0.2.0 migration

The pre-1.0 minor release includes breaking host API cleanup: rename `ExecutionProtection` to `ExecutionProtections`, and pass `requiredProtection` before the final `cancellationToken` in `BindAsync` and `PrewarmAsync` (named arguments are recommended). Rebuild consumers and regenerate installation declarations for the exact 0.2.0 core package matrix; the wire protocol and host compatibility level remain 1. Python and TypeScript author SDKs remain 0.1.0.

This release fixes cancellation-source ownership and shutdown/startup races, uses exclusive private native-socket directories, and selects Docker by an absolute trusted CLI path. Set `DockerProfile.DockerExecutable` for nonstandard CLI installations; PATH lookup is no longer used. All 37 original Sonar issues and both security hotspots were resolved before preparing this release. Platform validation limits remain documented in [platform qualification](platform-qualification.md).

The [0.2.0 release report](../reports/release/0.2.0/README.md) retains publication verification and links to durable package, symbol and qualification assets.

## 0.2.1 package branding

All core packages embed the existing website logo as their NuGet icon. Release export requires the embedded image to match the repository asset byte for byte. This patch changes package branding and exact compatibility versions; runtime APIs remain unchanged from 0.2.0.

The [0.2.1 release report](../reports/release/0.2.1/README.md) records exact public package and gallery icon verification, with durable qualification artifacts.

## 0.3.0 optional MCP tools

Hosting adds explicitly selected local MCP 2025-11-25 and 2026-07-28 tools alongside native plugins. Discovery and calls share worker admission, tenant ownership, deadlines and cleanup. Native remains the default; there is no new MCP runtime dependency. Unsupported result continuations are rejected even if a server also supplies content. See the [MCP guide](mcp-plugins.md) and [executable example](../examples/mcp/README.md).

Update all four core packages and exact installation declarations together. Native author SDKs remain 0.1.0 and are still source-built. Hosting uses Microsoft logging/DI abstractions 10.0.12; the optional gateway's build-only Grpc.Tools is 2.84.0. Remote MCP, arbitrary server SDKs and additional platform qualifications are outside this release.

The [0.3.0 release report](../reports/release/0.3.0/README.md) links the original qualified packages, symbols, test evidence and measurements of the released Hosting assembly.

## 0.3.1 named MCP methods

Hosting exposes `McpMethods.ListTools` and `McpMethods.CallTool` for consumer invocations. Examples and the guide use these constants. Internal MCP identifiers, SDK operations and gateway metadata are centralized without changing wire values. Update the four core packages together to match the exact 0.3.1 compatibility matrix.

## 0.5.0 composition and gateway packages

Composition adds bound SDK mapping with authenticated remote tenant discovery and observable cleanup failures. Gateway splits hosting from the ASP.NET-independent remote client and adds direct HTTP/2 TLS qualification. See [gateway deployment](gateway.md), [composition](bulk-composition.md) and [dependency review](optional-dependencies.md). The [release audit](../reports/release/0.5.0/audit.md) records the integrated review and qualification boundary. Publication is completed only by the gated release workflow and public package verification.

Run `./scripts/prepare-core-packages.sh` to prepare all seven release packages in `artifacts/packages`, then `python3 scripts/verify-optional.py` for packed optional consumers. The clean candidate performs this qualification against frozen artifacts. Core installation declarations remain the three core host identities plus author SDKs; optional packages are never mandatory declaration entries.

Before tagging, review the seven-package allowlist and NuGet Trusted Publishing policy for the three new IDs. Export the original qualified packages and symbols; do not rebuild after qualification. After publication, verify all seven public package payloads against the candidate and retain the report.

## 0.4.0 approved session reuse and fair scheduling

The four core packages add memory-led fair scheduling, explicit operator-approved reuse, cleanup capability checks, idle retention and diagnostics. C# 0.4.0 and Python/TypeScript 0.2.0 SDKs register session-owned resources and attempt reverse-order cleanup. Customer-bound execution remains the default. Hidden globals and unregistered background work remain a documented residual risk under approval; see [best practices and runnable examples](reusable-plugins.md).

Clean candidate qualification now includes source and packed reuse consumers, matching Hosting/SDK DLL hashes, and all three maintained author examples. Download the exact Python wheel and TypeScript tarball from the 0.4.0 release assets and install them with `python -m pip install ./weaveport_sdk-0.2.0-py3-none-any.whl` and `npm install ./weaveport-sdk-0.2.0.tgz`. These packages are not announced as registry publications.

The [implementation evidence](../reports/verification/approved-session-reuse-20260920/README.md) retains pre-release cold/warm measurements with their original hashes and version labels. It does not establish maximum Docker capacity. Docker cold-start and repeated image resolution at registration remain explicitly separate from warmed invocation throughput.

The [0.4.0 audit](../reports/release/0.4.0/audit.md) records release findings and their corrections. Publication evidence is added only after successful gated delivery.
