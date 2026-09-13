# GitHub and NuGet releases

The GitHub delivery target is [yesbert/WeavePort](https://github.com/yesbert/WeavePort). CI verifies documentation and release tooling, and runs the existing frozen-candidate qualification on a standard macOS ARM runner. This is functional qualification, not a hosted performance benchmark or Windows support claim.

## Packages and version

The release allowlist in `build/release-packages.json` contains `WeavePort.Abstractions`, `WeavePort.Hosting`, `WeavePort.Sdk` and `WeavePort.Sdk.Client`. Gateway, Composition and Testing remain optional/experimental; adding them requires appropriate package-consumer evidence. Python and TypeScript author SDK publication is separate from NuGet delivery.

The current version `0.1.0-internal.2` is an internal candidate and is deliberately rejected by the public release gate. Setting up Actions does not publish it or allocate a public version. Before the first public prerelease, review the version change across `Directory.Build.props`, the exact compatibility matrix, package references, example installation metadata, API expectations and documentation. Requalify the complete candidate. A simple tag override would leave incompatible declarations and is therefore refused.

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
