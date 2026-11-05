# Release 0.10.0 audit

## Scope

Function files ([PR #55](https://github.com/yesbert/WeavePort/pull/55)), the SonarQube follow-up ([PR #57](https://github.com/yesbert/WeavePort/pull/57)) and the version allocation ([PR #56](https://github.com/yesbert/WeavePort/pull/56)). The design, decisions and interim evidence are in the [archived change](../../../openspec/changes/archive/2026-11-05-add-function-file-transfer/verification.md).

## Review findings

- **Own high-effort code review:** eight findings, all fixed before merge. They covered:
  - a gateway timeout reported as cancellation;
  - Python name validation that accepted a trailing newline;
  - stream operations with files on scheduled bindings;
  - empty reads not charged against the read budget;
  - a Python synchronous output completed from garbage collection;
  - double block encoding, which made the C# copy 208 ms instead of 38 ms;
  - worker starts just to refuse a call;
  - duplicated validation.
- **Copilot:** five review rounds with sixteen findings, all fixed, answered and resolved before merge. Among them were bounded writer cleanup, cleanup failures after commit, the unary limit before commit, late reads from caches and buffers, caller-side read budgets, one pending gRPC write at a time, and legacy failure codes. Later commits could not be reviewed by Copilot because its quota was exhausted; the maintainer's own review covered them.
- **SonarQube:** the first main analysis after #55 failed its gate with 22 maintainability issues and 76.3 % coverage on new code. #57 resolved every issue and added the file suites and a wire-level C# file flow to the coverage run. The gate passed with 0 issues, 0 hotspots and 83.7 % coverage on new code; the tagged commit's report is under [sonar/](sonar/summary.md).

## Compatibility

All public API changes are additive, and both reviewed API baselines changed only by additions. Host API 2 and native protocols 1/2 are unchanged; the file capability is negotiated per call. The independent publisher fixture was re-signed for the 0.10.0 matrix with a temporary, discarded key. Consumer lockfiles changed only their WeavePort entries.
