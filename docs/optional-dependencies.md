# Optional package dependency review

Introduced with the 0.5.0 release; these dependency identities remain in the 0.9.1 package family and were checked against the current central package declarations on 2026-10-31. `compatibility/optional-dependencies.json` records exact NuGet dependency declarations, source restore closures and license expressions. The optional package verifier checks these identities against actual restored artifacts. WeavePort source remains MIT; dependencies keep their own licenses.

| Dependency | Version | Use | License and redistribution obligations |
|---|---|---|---|
| Grpc.AspNetCore.Server | 2.85.0 | Server runtime | Apache-2.0; retain license, attribution and any applicable notices; identify modifications if made |
| Grpc.Net.Client, Grpc.Net.Common, Grpc.Core.Api | 2.85.0 | Client/server runtime | Apache-2.0; same obligations |
| Google.Protobuf | 3.36.2 | Protocol runtime | BSD-3-Clause; reproduce copyright, conditions and disclaimer; no endorsement |
| Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.1 | Standalone client transitive closure | MIT; retain copyright/license and bundled third-party notices |
| Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | Hosting consumer closure | Existing core MIT review; higher compatible versions selected when Hosting is present |
| Grpc.Tools | 2.85.0 | Build only, PrivateAssets=all | Apache-2.0 package metadata; upstream combined license includes bundled tool component terms. Tools/binaries are not shipped in WeavePort runtime packages |

License text is retained with normalized text whitespace/line endings. Exact source provenance comes from the NuGet nuspec repository commit:

- gRPC .NET `a7767e28a2c2f4e89a17e9efb95f75e8a82637d0`: [license](https://github.com/grpc/grpc-dotnet/blob/a7767e28a2c2f4e89a17e9efb95f75e8a82637d0/LICENSE), retained in [grpc-dotnet-LICENSE.txt](licenses/grpc-dotnet-LICENSE.txt).
- Protobuf `2c74169b34066ceb8ddb6b882fcb3fb32d737a55`: [license](https://github.com/protocolbuffers/protobuf/blob/2c74169b34066ceb8ddb6b882fcb3fb32d737a55/LICENSE), retained in [protobuf-LICENSE.txt](licenses/protobuf-LICENSE.txt). Its explicit generated-code clause assigns generated code to the input-file owner; the runtime retains its BSD license.
- gRPC tools `cc92f57677a4b8283821affef0648e8c9a6ff9a8`: [combined tool license](https://github.com/grpc/grpc/blob/cc92f57677a4b8283821affef0648e8c9a6ff9a8/LICENSE), retained in [grpc-tools-LICENSE.txt](licenses/grpc-tools-LICENSE.txt).
- Microsoft 8.0.1 nuspec commit `9f4b1f5d664afdfc80e1508ab7ed099dff210fbd`: exact package [MIT license](licenses/dotnet-8-license.txt) and [third-party notices](licenses/dotnet-8-third-party-notices.txt) are retained from the NuGet archives.

The checked gRPC/Protobuf nupkgs declare SPDX expressions rather than embedded license files. Their exact source licenses are retained here and included with the gateway client package as supporting attribution. No third-party runtime DLL is embedded in a WeavePort nupkg; NuGet resolves dependencies separately. Applications redistributing a published output must preserve the dependency licenses/notices with those binaries. No source or binary modifications to these external dependencies are made. This is a dependency/notice review, not a vulnerability assessment or an assertion that every dependency is MIT.

## Core Hosting runtime dependencies

Portable runtime declarations added dependencies in 0.7.0 beyond the earlier logging-only Hosting closure. [External package identities](../compatibility/external-packages.json), the central package declarations and restored lockfiles record the current versions:

| Package | Version | Responsibility | Retained license |
| --- | --- | --- | --- |
| Tomlyn | 2.10.1 | Parse Python project TOML declarations | [BSD-2-Clause text](licenses/tomlyn-LICENSE.txt) |
| Chasm.SemanticVersioning | 2.8.2 | Interpret npm-compatible runtime ranges | [MIT text](licenses/chasm-LICENSE.txt) |
| Chasm.Formatting | 2.4.0 | Transitive formatting dependency of semantic versioning | [MIT text](licenses/chasm-LICENSE.txt) |
| Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | Caller-owned logging and its dependency | MIT package metadata and upstream notices |

Hosting includes the retained Tomlyn and Chasm license files in its package. These are runtime dependencies of Hosting, not dependencies introduced into the Python or TypeScript SDKs. The package refresh retains the reviewed license expressions. Consumers should retain the notices supplied with the exact redistributed artifacts.

Tomlyn 2.10.1 license text was checked at its nuspec source commit [6cc82dd](https://github.com/xoofx/Tomlyn/blob/6cc82dd23a2c0aefb858f92a51c1b184e2812103/license.txt). The updated runtime retains BSD-2-Clause.
