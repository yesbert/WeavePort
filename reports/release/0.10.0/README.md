# WeavePort 0.10.0 preparation

Status: prepared version allocation. Not tagged or published. The latest published release remains [0.9.1](../0.9.1/README.md).

This release includes function files, merged in [PR #55](https://github.com/yesbert/WeavePort/pull/55). All seven .NET packages use 0.10.0, and the Python and TypeScript author SDKs use 0.5.0. Host API 2 and native protocols 1/2 remain unchanged; the file capability is an additive ready field and invoke extension.

The feature change was qualified on its own branch: 78 stages, recorded in [its verification notes](../../../openspec/changes/archive/2026-11-05-add-function-file-transfer/verification.md). This preparation re-qualifies the committed version allocation in a fresh checkout before tagging.

The first SonarQube analysis after the merge failed its quality gate with 22 maintainability issues and 76.3 % coverage on new code. [PR #57](https://github.com/yesbert/WeavePort/pull/57) resolved the issues and added the file suites to the coverage run. The analysis of `e1fc503` passes the gate with 0 issues, 0 hotspots and 83.7 % coverage on new code.

See [release and migration notes](../../../docs/releases.md#0100-function-files) for upgrade steps, gateway registration limits and the new failure codes. No new dependency, sandboxing, platform qualification or performance promise is included.

Publication follows the [release procedure](../../../docs/releases.md#release-procedure): after review and merge, an annotated `v0.10.0` tag starts the protected workflow, which repeats clean qualification; NuGet publication then waits for approval of the `nuget-org` environment. Exact-tag qualification and public downloads are verified before availability is announced.
