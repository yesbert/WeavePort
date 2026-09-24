# Decision Room

A small executable integration template: Alice and Bob evaluate three improvement proposals. Plugins calculate evaluations and the winning proposal; the application controls the workflow, grants knowledge access and persists committed evaluations. No model, account, database, Docker engine or sibling product is required.

## Run

From the repository root, with the .NET SDK from `global.json` and Python 3.11+ with `venv`/`pip` available:

```sh
./scripts/decision-room.sh --build
```

The first build packs WeavePort libraries, restores the host and C# worker from those NuGet packages, builds/installs the Python SDK wheel into a private environment, and runs the example. Package build dependencies may require internet access. Later runs need no SDK package download:

```sh
./scripts/decision-room.sh --resume
```

The default journal is `artifacts/decision-room/runs/default.json`. A new run never overwrites an existing journal. Use `--journal artifacts/decision-room/runs/another.json` to start another one. The runner starts and stops only its own trusted native workers.

Expected decision:

| Proposal | Alice: economy | Bob: impact | Total |
|---|---:|---:|---:|
| A — Improve documentation | -6 | 9 | 3 |
| B — Automate support | -14 | 25 | 11 |
| C — Build analytics | -30 | 27 | -3 |

With version 1, the winner is **B**. Each evaluation prints the cost, benefit, retrieved risk, score and knowledge profile. Version 1 scores are `benefit × benefitWeight − cost × costWeight − risk × riskWeight`. Version 2 multiplies the risk penalty by ten and selects A for this fixture. The room sums scores and breaks ties by ordinal proposal ID. Completion requires both evaluations. There is no random source or semantic clock in this finite example.

## Change behavior through configuration

Copy `samples/DecisionRoom/config.json` to an example-owned location, edit it and pass `--config PATH --journal NEW_PATH`. Each participant selects `language` (`csharp` or `python`), a knowledge `profile` and integer priorities. With both participants using Alice's priorities, A wins; with both using Bob's priorities, C wins. Changing language alone preserves the result. Knowledge belongs to the configured tenant and profile, not to fields supplied in a callback request.

## Follow recovery

```sh
./scripts/decision-room.sh --journal artifacts/decision-room/runs/paused.json --pause-after-first
./scripts/decision-room.sh --journal artifacts/decision-room/runs/paused.json --resume
./scripts/decision-room.sh --journal artifacts/decision-room/runs/restarted.json --restart-after-first
```

The host commits an evaluation only after validating it and the resulting room transition. Resume initializes a fresh room and replays committed evaluations. A crash before commit allows this deterministic, side-effect-free evaluation to be recomputed. An exclusive lock prevents concurrent writers to one journal. A schema, configuration or selected built-artifact mismatch refuses resume. The journal records artifact hashes; do not replace native artifacts during a run. Rebuilding changed artifacts can intentionally invalidate an old journal.

This is local single-writer recovery, not power-loss durability, distributed coordination, secure package installation or an exactly-once promise for external actions.

## Observe denied knowledge access

```sh
./scripts/decision-room.sh --journal artifacts/decision-room/runs/denied.json --deny-knowledge
```

This deliberately exits nonzero: the strategy requests `knowledge.read` without its grant, the callback is not executed, and no evaluation is committed for that call. Run again with the same journal and `--resume` without the denial option to continue normally.

## Integration structure

- `Contracts`: sample-owned domain records; no WeavePort dependency.
- `Plugin`: C# SDK functions for room initialization/reduction/snapshots and evaluation. Distinct room and strategy bindings launch separate workers from the same artifact.
- `Python`: an equivalent strategy through the packaged Python SDK.
- `Host`: configuration, scoped callback, worker binding, state validation, persistence and console walkthrough.
- `Host/Verification/Verification.cs`: explicit `--verify` harness, including abrupt termination of an owned worker. Crash controls are absent from the plugin contract.

Both host and C# plugin reference packed WeavePort artifacts. Their only project reference is the sample's own contract module. No HiveWeaver, NextPA, TreeWeaver, Stratara or LoomWeaver dependency is introduced. This is an integration template inspired by HiveWeaver's extension boundaries, not a HiveWeaver adapter or parity demonstration. Application data and rules remain outside WeavePort core.

## Verify

```sh
./scripts/decision-room.sh --build --verify
```

The suite checks expected winners, both languages against the same fixtures, pause/resume, explicit restart, abrupt worker loss after commit, callback denial in both languages, concurrent customers and same-customer profiles, incompatible resume and exclusive journal ownership. Results and journals are retained in a unique directory under `artifacts/decision-room/verification/`.

Plugin release selection is described below. The current verification is native macOS on .NET 10 with C# and Python. It does not qualify Windows, Linux, Docker execution, remote workers, hostile plugins or full application migration. Native workers run trusted code under the user's OS identity; callback grants are not an OS sandbox. The existing broader PoC suites remain separate.

## Parallel plugin versions

The build creates actual release-1 and release-2 C# binaries and Python modules in separate `artifacts/decision-room/releases/` directories. The author SDK declares each release during startup, and the host checks it. Each evaluation also carries its release identity.

```sh
./scripts/decision-room.sh --build --journal artifacts/decision-room/runs/version-one.json --version 1 --pause-after-first
./scripts/decision-room.sh --activate 2
./scripts/decision-room.sh --journal artifacts/decision-room/runs/version-two.json
./scripts/decision-room.sh --journal artifacts/decision-room/runs/version-one.json --resume
```

The new run uses version 2 and selects A. The resumed run still uses version 1 and selects B. `--activate 1` returns the default for future runs to version 1. `--version 1` or `--version 2` explicitly selects a release for a new run; the JSON configuration can alternatively set `pluginVersion`. An explicit conflicting selection on resume is refused.

Activation changes only `active-version.txt`. A run resolves it once, stores its version in its journal, and uses that version for every subsequent binding and restart. Resume reads the pinned version rather than the active default. Changes to another release do not invalidate the selected release's hashes. Missing, changed or incorrectly labelled selected artifacts are refused; there is no automatic fallback.

`--verify` also holds a version-1 worker live at a committed boundary while activating version 2 and completing a second run, then replaces the original worker and checks its version-1 result. It checks both languages in both releases and uses an isolated selector and artifact copies for fault tests, leaving normal deployment files untouched.

**Development boundary:** build and activation are different actions. `--build` is an offline development rebuild and can replace artifacts/shared SDK files; stop active example runs before rebuilding. Activation of already-built releases is the supported live operation. This is not a production package installer or a live host/SDK upgrade mechanism. Native runtimes and selected artifact bytes must remain stable while executing.

**Journal schema:** this version uses schema 2. Journals from the initial development slice (schema 1) are refused without modification. Retain that original build to replay them, or start a new journal. Schema 2 prevents default activation from changing an existing run's selected release.

## Shared installation identity

This sample now uses the packaged [installed-plugin resolver](../../docs/installed-plugins.md). Builds seal release directories; resolution validates content and contract identity. Activation changes future operations only. Do not modify or rebuild executing release/runtime files. Manifest sealing uses the .NET Hosting API and requires no Python. Decision Room still requires Python for its Python strategy provider.

Journals created before this resolver lack its manifest digest and are refused unchanged. Use a fresh `--journal` or retain the original build for those development journals.
