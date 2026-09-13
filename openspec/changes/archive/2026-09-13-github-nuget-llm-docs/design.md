## Context

WeavePort targets .NET 10 with SDK 10.0.401 and an exact four-package compatibility matrix. The first public version is 0.1.0 under MIT. The historical internal candidate retains its original identity.

## Decisions

- Start public history from one reviewed source baseline. Keep current specifications and open changes; pre-public archived changes are excluded once. Future archives remain normally tracked.
- Keep local development context out of tracked files and validate that public CI works from an independent clone.
- Use native macOS ARM qualification because it matches the currently verified product topology. Do not infer Windows qualification from build success.
- Release repeats clean qualification, then exports the original tested package and symbol artifacts. Rebuilding after verification could publish different bytes.
- Require exact tag/version agreement, confirmed package licensing, package allowlisting and qualified artifact hashes. Refuse internal release tags.
- Use OIDC only in the NuGet publish job and an environment reviewer. The separate announce job alone receives repository write permission.
- Use SDK-provided Source Link rather than adding a production dependency.
- Generate LLM documents from maintained guides, specifications and reviewed public API signatures. They are retrieval files, not MCP endpoints. Open proposals are not included as implemented guarantees.

## Verification and limits

Clean candidate qualification has passed 866 assertion executions and retained 139 frozen files. Seven release-gate tests check version, license, candidate identity and artifact tampering. Hosted macOS qualification also passed before the public baseline reset; the new baseline must be qualified independently.

The reviewed API snapshot includes the already implemented OldestQuarantineSeconds property. Hosting test fixtures use the selected dotnet host instead of relying on a globally installed apphost runtime. These are verification corrections, not new runtime behavior.

## Release outcome

The first MIT-licensed public release is [v0.1.0](https://github.com/yesbert/WeavePort/releases/tag/v0.1.0), commit `98b22648620478cb2d28a7d5eb4cbebb948fdac6`. The [release workflow](https://github.com/yesbert/WeavePort/actions/runs/34770638886) passed all qualification, publication and announcement jobs. It qualified 866 assertions and 139 frozen artifacts, validated package license/source/hash metadata, and published the four allowlisted NuGet packages and symbols through OIDC. Original packages, symbols, qualification evidence and checksums are attached to the release for durable retrieval.

No runtime/API surface was changed for release. Historical internal distribution and measurements retain their original versions. Windows and optional package publication remain outside this release.

## Sources

- https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing
- https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/source-link
- https://learn.microsoft.com/en-us/dotnet/core/deploying/
- https://llmstxt.org/
