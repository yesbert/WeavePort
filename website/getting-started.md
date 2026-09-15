---
title: Run your first plugin workflow
description: Run Decision Room, see C# and Python plugins score proposals, and follow the application from granted knowledge access to a saved result.
---

# Run your first plugin workflow

See WeavePort in a complete application: two participants evaluate three proposals using C# or Python plugins. The host grants knowledge access, validates the evaluations and saves the decision. You will finish with a result you can inspect and a host you can adapt.

> **Expected result:** with the default version 1 configuration, **B — Automate support** wins with a total score of **11**.

## Before you start

WeavePort supports Windows, Linux and macOS for trusted stdio execution. This walkthrough uses a Bash runner with Unix virtual-environment paths and has been validated on macOS arm64. Linux can use the same source workflow, with release validation pending. For native Windows, use equivalent .NET build steps and Windows interpreter paths; this Bash runner is not a native Windows launcher. See [platform support and validation](../docs/platform-qualification.md).

Install Git, the .NET SDK selected by [global.json](../global.json) (currently 10.0.401) and Python 3.11+ with `venv` and `pip`.

The first build restores packages from the network. The example runs locally without an external database, an account or a Docker service. Native plugins run as trusted code with your OS-user rights.

## 1. Clone and run

```sh
git clone https://github.com/yesbert/WeavePort.git
cd WeavePort
./scripts/decision-room.sh --build
```

The runner packs the platform libraries, builds the host and C# worker, installs the Python SDK into a private environment and runs the example. It consumes packages built from that checkout. To inspect the exact public release source, select the `v0.3.0` tag; main may contain later work.

## 2. Check the result

With the default configuration, the application combines the participants' evaluations:

| Proposal | Combined score |
|---|---:|
| A — Improve documentation | 3 |
| B — Automate support | **11** |
| C — Build analytics | -3 |

B wins. The [sample walkthrough](../samples/DecisionRoom/README.md) explains the calculation and the knowledge profiles behind it.

## 3. Follow one plugin call

1. The application selects a plugin artifact and binds a participant's context.
2. The plugin requests knowledge through a granted callback.
3. The host returns knowledge scoped to that participant.
4. The plugin calculates an evaluation.
5. The application validates and commits the result to its journal.

This is the extension point you can reuse: plugin-specific logic, application-owned access and durable results.

## 4. Make it your own

Read the sample's [configuration guide](../samples/DecisionRoom/README.md#change-behavior-through-configuration) to switch languages or scoring priorities. Use a separate journal for a new configuration. The host keeps the workflow while plugin selection changes the evaluation strategy.

To resume the existing run or execute the verification fixtures:

```sh
./scripts/decision-room.sh --resume
./scripts/decision-room.sh --build --verify
```

A fresh run refuses to overwrite an existing journal. Pass `--journal artifacts/decision-room/runs/another.json` for a separate run. Rebuilding changed artifacts can intentionally invalidate earlier journal identity checks. Verification uses dedicated fixtures, including worker termination and recovery.

## Choose your next example

<div class="wp-start-links"><a href="../samples/DocumentWorkshop/README.md"><strong>Process documents →</strong> <span>Swap readers while the application controls source access and committed results.</span></a> <a href="../samples/AppointmentDesk/README.md"><strong>Make scheduling extensible →</strong> <span>Follow replaceable rules, shared admission and recovery after an interrupted action.</span></a></div>

Ready to integrate? [Write a plugin](../docs/plugin-sdk.md), [embed one coordinator](../docs/embedded-coordinator.md) and [select installed artifacts](../docs/installed-plugins.md). See [packages and support](packages.md) for release and deployment boundaries.
