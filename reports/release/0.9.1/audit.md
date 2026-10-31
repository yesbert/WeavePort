# Preparation audit

The V2 dependency workflow was adapted with registry failure propagation, literal stable versions, explicit pins, dry-run isolation and restoration after npm resolution failures. Six focused updater regressions pass. Internal version allocation and historical evidence remain outside automatic dependency updates.

Tomlyn 2.10.1 required replacement of its removed ToModel API. Sealing and independent publisher signature tests pass with JSON reflection disabled; sealing also explicitly disables Tomlyn reflection. Public API baselines are not changed. The exact 0.9.1 / author SDK 0.4.1 matrix and signatures were independently regenerated with a discarded temporary RSA key.

The complete updater build, portable installation checks, TypeScript tests and transitive NuGet/npm vulnerability audits passed on 2026-10-31 with zero reported vulnerabilities. Runtime and optional dependency license expressions and exact source notices were reconciled; see [dependency review](../../../docs/optional-dependencies.md). npm tool/test closures declare MIT or Apache-2.0. The prior main analysis at 9c161471b1b4808a58d7a9db45a30ce0d1cb974b has zero open SonarQube issues and zero unreviewed hotspots. A fresh analysis of merged source is required before tagging.

Committed candidate qualification and CI results are pending. No independent Copilot review or publication is claimed. Docker services were not changed.

Development-only tool licenses were inspected in the exact packages: DocFX 2.81.0 is MIT with bundled notices; setuptools 84.2.1 declares MIT; SonarScanner 12.0.0 embeds LGPL-3.0 and component licenses; dotnet-coverage 18.12.0 embeds Microsoft's .NET Library terms permitting development/test use. These tools are not redistributed in release packages. Existing coverage telemetry opt-out remains set.

OpenSpec 1.16.0 newly enforces requirement length under strict validation. Nine existing requirement summaries were shortened without removing scenarios or changing required behavior; all 23 specs/changes pass the new validator. The website navigation regression now targets release-091.
