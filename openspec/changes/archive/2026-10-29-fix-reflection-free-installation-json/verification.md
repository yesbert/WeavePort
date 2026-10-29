# Verification — 2026-10-29

Environment: macOS, .NET SDK 10.0.403 (global.json patch roll-forward), net10.0, C# 14.0.

- Before production changes: the dedicated reflection-disabled executable failed with exit 134 at DotnetRequirements.Read, reporting a valid worker.runtimeconfig.json as invalid because implicit JSON reflection was disabled.
- After changes: the same executable passed .NET sealing, byte-stable repeat sealing, synchronous/asynchronous pinned resolution, Python/Node offline sealing and invalid-input preservation.
- Source and freshly packed Hosting regression suites passed, including the previous-serializer byte comparison and independent pinned-manifest fixture.
- Fresh core NuGet packages were built. The reflection-disabled executable passed against a fresh isolated package cache through scripts/verify-portable.sh; packaged portable sealing and actual .NET invocation also passed with the same installation identity.
- The portable script's language stage initially refused system Python 3.9.6, correctly enforcing the fixture's >=3.11 requirement. Re-running that stage against the same packaged host with installed Python 3.12 passed all Python/Node ownership modes, strict-runtime refusals and initial/replacement runtime-change refusals. Docker was not used or reconfigured.
- Full C# readability/control-flow gate, architecture check, public-tree check, documentation links and strict OpenSpec validation passed. Generated AI documentation was refreshed and checked.

The uncommitted working tree was tested directly and through locally packed artifacts. The full committed-HEAD candidate workflow (scripts/verify.sh) was not run: it would qualify the prior committed source, not this working tree. This evidence does not claim general Native AOT publishing support or new platform qualification.
