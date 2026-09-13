# Verification evidence

## Initial slice

Verified on 2026-09-12 with SDK 10.0.401, net10.0/C# 14 and Python 3.14.7 on the local macOS machine. Both host and C# worker were published against freshly packed NuGet dependencies; Python used the built and installed SDK wheel. WeavePort core source was unchanged.

Command: `./scripts/decision-room.sh --build --verify`.

The completed suite printed 20 passing assertions covering:

- Default B, cost-oriented A and benefit-oriented C winners.
- C#-only and Python-only substitutions matching the mixed-language baseline.
- Identical replay of a completed journal, pause/fresh-host resume, explicit worker replacement and actual process kill after the first committed event.
- Missing-grant refusal in both languages with zero callback executions and zero committed evaluations.
- Independent concurrent customers, separate knowledge profiles, and two concurrent runs of the same customer.
- Configuration mismatch without overwrite, exclusive journal writing and artifact identity mismatch.

Retained local evidence directory: `artifacts/decision-room/verification/c3fc3dc30bec43d48e9233816f8ffbcd/`. Generated artifacts are intentionally ignored; the suite creates a fresh evidence directory on reproduction.

The normal CLI walkthrough was also run with `--restart-after-first`, and the missing-grant demonstration was followed by successful `--resume`. Strict OpenSpec validation and whitespace checks passed. Build emitted existing package-readme advisories for SDK packages; no compiler warnings/errors occurred in the sample.

No benchmark, remote execution, Docker, Windows/Linux, hostile-code isolation or original-product integration claim is made by these checks. Existing PoC evidence and the open Windows qualification task retain their own scope.

## Parallel-version slice

On 2026-09-12 the expanded Decision Room suite passed 33 assertions, including release activation while an older run remains live, restart/resume with a pinned version, both languages in release 2, changed/unavailable selected artifacts and unrelated-release changes. Evidence: `artifacts/decision-room/verification/807051422b16415884a27c6c02e7a859/`.

A separate packed-SDK suite passed 12 C#/Python/TypeScript checks for omitted, declared, mismatched and empty startup versions. This slice corrects the SDKs' previous hardcoded version-1 declaration while keeping host startup validation intact. See the [retained report (historical) — pre-public record](../../docs/history.md) and `./scripts/sdk-versions.sh`.

The earlier no-core-source-change statement describes the initial slice only; this follow-up adds an SDK author setting. The host/runtime isolation and scheduling code remain unchanged. Schema-1 development journals require their original build; schema 2 records the selected release explicitly.
