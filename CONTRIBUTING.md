# Contributing

## Development tooling

Use the .NET SDK pinned in `global.json`, Python and Node. Install OpenSpec globally; it is not a repository dependency. Validate maintained specifications with:

```sh
openspec validate --all --strict
```

Read [engineering guidelines](docs/engineering.md), [framework guidance](docs/dotnet-guidance.md) and the applicable product specifications before changing behavior.

## Specifications and changes

Current product requirements live in `openspec/specs/`. Proposed work lives in `openspec/changes/`, with a proposal, design, specification deltas where behavior changes, and verifiable tasks. Tooling and documentation changes with no product behavior change use `skip_specs: true` in their `.openspec.yaml`.

Discuss material scope changes before implementation. Keep decisions, alternatives and verification limits in the change design. After implementation and verification, synchronize deltas into the current specifications and archive the change under `openspec/changes/archive/`. Future archives are normal tracked repository contents. The public initial commit intentionally excludes the pre-public development archives; see [historical evidence](docs/history.md).

## Verification and pull requests

Submit focused English pull requests against `main`. Run `openspec validate --all --strict`, `git diff --check`, documentation/public-tree checks and tests appropriate to the changed behavior. Public contracts require packed NuGet consumer verification.

```sh
python3 scripts/check-public-tree.py
python3 scripts/generate-llms.py --check
python3 tests/documentation/check-links.py
./scripts/verify.sh
```

The final command qualifies committed HEAD in a fresh checkout. See [test entry points](tests/README.md) for focused checks and optional Docker adapter tests. Do not interrupt unrelated services for tests. Benchmarking uses the separately qualified distribution and [measurement protocol](docs/benchmarking.md); build-only runs are not measurements.

Keep credentials, private notes and machine-local configuration out of contributions. Package version allocation and publication are explicit release operations; follow the [release guide](docs/releases.md).
