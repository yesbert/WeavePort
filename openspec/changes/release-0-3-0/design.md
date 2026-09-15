## Decisions

Use 0.3.0 to make optional MCP adoption visible in the pre-1.0 release line. Keep the four-package allowlist, MIT license, native protocol and host compatibility level 1. Keep historical benchmark and release reports unchanged. No new MCP runtime dependency.

Reject explicit non-complete resultType even when a server also supplies content: the original fixture lacked content and did not expose this inconsistent envelope. Reproduction failed with `fault interaction-content: ok`; the same process-level case must pass after correction.

Official sources reviewed 2026-09-15: https://modelcontextprotocol.io/specification/2026-07-28/server/tools ; https://modelcontextprotocol.io/specification/2026-07-28/server/discover ; https://modelcontextprotocol.io/specification/2026-07-28/basic/transports ; https://ts.sdk.modelcontextprotocol.io/v2/protocol-versions ; https://dotnet.microsoft.com/en-us/download ; https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.trygetproperty?view=net-10.0 . Registry metadata confirms SDK 2.0.0, Zod 4.6.5, TypeScript 7.0.2, and DocFX 2.78.5.

## Release gates

Fresh committed candidate, source and packed tests, official SDK interoperability, documentation build, required GitHub checks, normal nuget-org environment and Trusted Publishing. Never move a released tag.

## Dependency review

NuGet flat-container metadata identifies Logging.Abstractions 10.0.12 and Grpc.Tools 2.84.0 as newer stable versions. The exact net10.0 shipped closure is Logging.Abstractions and DependencyInjection.Abstractions 10.0.12 (MIT); original packages retain their Microsoft copyright and THIRD-PARTY-NOTICES.TXT. No third-party code is relicensed. Reviewed original package SHA-256: logging 34eaaa60afb61ed3d86e2e2669eae0c690d0e237e8a7066ba5bdb45a386709e7; DI 4664fab0ac4b56b8935333f59ee9e7801c8ab96de7c57cb3b6eb8192a5a61c55.

Grpc.Tools 2.84.0 is Apache-2.0, development-only with PrivateAssets=all, no package dependencies; its compiler is not distributed in the four core packages. Original package SHA-256: 1bfc81e6a218d71a9d9b0569ec50b8e27697c5fa89a7bef926424434b0802d96. Preserve Apache license/notice obligations if redistributing that tool separately. gRPC runtime 2.83.0 and Protobuf 3.36.1 remain current. No preview framework or major runtime upgrade is introduced. MCP fixture npm audit reported zero known vulnerabilities on 2026-09-15, which is not a security certification.

Python packaging backend setuptools updated from 80.9.0 to stable 84.0.0 (PyPI metadata; requires Python >=3.10). Exact wheel SHA-256 51a52592b3b99e102b609654876bd65f19f999935166d1352678931132b0c670; its LICENSE retains MIT terms. This is build-only, not a wheel runtime dependency.
