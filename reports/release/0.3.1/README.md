# Public release 0.3.1

The four core packages at annotated tag `v0.3.1`, commit `23d6dbb119c845fa913b05fc20a62267c4ac6bc6`, add public `McpMethods.ListTools` and `McpMethods.CallTool` constants. Existing literal calls remain compatible. Internal protocol names are centralized without changing wire values or behavior.

- [Release](https://github.com/yesbert/WeavePort/releases/tag/v0.3.1)
- [Release workflow](https://github.com/yesbert/WeavePort/actions/runs/34955711213)
- [Original packages and symbols](https://github.com/yesbert/WeavePort/releases/download/v0.3.1/weaveport-0.3.1-packages.zip)
- [Qualification evidence](https://github.com/yesbert/WeavePort/releases/download/v0.3.1/weaveport-0.3.1-evidence.zip)
- [Package hashes](release-manifest.json) and [qualification summary](qualification.json)

## Verification

The exact release tag passed 1,088 assertions and froze 139 artifacts. Packed consumers exercise public MCP constants against both supported MCP revisions, native SDK behavior, gateway regressions and exact package/API compatibility. Required Windows, Linux, macOS, documentation and CodeQL checks passed. The main-commit [Sonar quality gate](https://github.com/yesbert/WeavePort/actions/runs/34955697786) passed before publication approval.

All four package hashes were checked against the qualified feed before approving the normal nuget-org environment gate. OIDC Trusted Publishing uploaded the original packages and sibling symbols. No long-lived API key or release-gate bypass was used.

Public NuGet downloads of all four packages were verified after indexing. Every ZIP entry matches the qualified original except NuGet’s added repository signature. Embedded icons and MCP readmes also match. See [public download verification](publication-check.json). The website serves the release commit and the DEV draft now uses the 0.3.1 example without source-only instructions.

## Scope

No dependency upgrade, transport change, new MCP feature or platform qualification is claimed. The [0.3.0 benchmark report](../0.3.0/README.md) remains evidence for that assembly; this patch makes no new performance measurement claim.
