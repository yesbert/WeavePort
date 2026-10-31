# Dependency updates

Run `./scripts/update-all-dependencies.sh --dry-run` to resolve the latest stable registry versions without writes, installs, builds or audits. Then run `./scripts/update-all-dependencies.sh` to apply the reviewed plan, restore tools, build, run affected tests and audit NuGet/npm dependencies. `--skip-build`, `--skip-tests` and `--skip-audit` support explicitly staged work; skipped checks must run before release.

The workflow adapts the NextPA-V2/HiveWeaver-V2 update sequence to WeavePort. It discovers maintained tracked manifests: external central NuGet packages, npm projects, exact Python build requirements, local .NET tools and pinned CI tools. Historical reports, specifications and fixtures are excluded. Internal WeavePort package references and local SDK tarball references remain part of deliberate release allocation. Framework/runtime and GitHub Actions updates are reviewed separately from registry packages.

All registry resolution completes before mutation. Registry errors fail the run. npm updates honor peer constraints and restore the original manifests/locks on resolution failure. The TypeScript SDK lock is refreshed first, then its dependencies are installed and a fresh SDK archive is built before resolving the local example; this prerequisite also runs with skip flags. Generated output and dependency caches are not rolled back. SDK tests/build precede portable checks so fresh checkouts never depend on stale dist output. NuGet restore and verification errors remain visible for compatibility repair. The report and NuGet audit are retained under `artifacts/dependency-update/`. The script supports the literal version forms currently maintained here; unsupported ranges or MSBuild properties require an explicit adaptation, not silent fallback.

## Pins

`.dependency-pins.json` holds exceptional versions. Each entry has `registry` (`nuget`, `npm` or `pypi`), `package`, exact `version`, `reason` and `review_when`; optional `path` scopes a hold to one manifest. A held version is restored if it drifts. Review conditions are informational and never executed as shell. Remove a hold only after verifying its reason no longer applies. No pins are implicit.

## Review and delivery

Inspect upstream API changes, exact package licenses/notices, vulnerability results and transitive closure. Update compatibility/API baselines only after comparing actual packed output with the intended change. Changed SDK artifacts need a new version even when their protocol is unchanged. Preserve old release evidence and independently regenerate exact compatibility fixtures and signatures where needed.

Run `python3 tests/dependencies/test_update.py`, strict OpenSpec/documentation checks and committed `./scripts/verify.sh`. The update-script tests also run in CI and candidate qualification. Main-only SonarQube must pass before tagging. Follow the [release procedure](releases.md) for separately authorized publication and public payload verification. The updater never operates Docker or publishes packages.
