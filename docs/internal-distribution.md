# Internal distribution 0.1.0-internal.2

This versioned delivery contains the qualified owner-controlled plugin platform and three reference applications for macOS arm64. The bundle version is distinct from component versions: four core NuGet packages are `0.1.0-internal.2`, the Python wheel and TypeScript npm package remain `0.1.0`. Their exact bytes come from the retained internal candidate qualification; packaging does not rebuild or relabel them.

## Prerequisites

The qualified environment is macOS 26.6.2 arm64, .NET shared framework **10.0.12** and Python **3.14.7**. The installer checks the qualified .NET and Python executable hashes in `distribution.json`. Only .NET 10.0.12 may be installed within the 10.0 runtime family for this delivery. Other major runtime families may coexist. Building the source templates also requires .NET SDK **10.0.401**. The optional TypeScript package was qualified with Node **26.8.2**; Node is not required to run these three applications.

Prerequisites must already be installed. Installation uses the bundled Python wheel with `--no-index --no-deps`; it downloads no packages. This is a framework-dependent internal delivery, not a clean-OS installer or a signed/notarized release. Do not disable operating-system protections to run it. Checksums detect changes against trusted metadata; they do not authenticate a hostile replacement or provide a process sandbox. Stable files and owner-controlled plugins remain required.

## Install and run

Verify the archive checksum against the separately supplied trusted checksum, extract it, then run from the extracted bundle directory:

```sh
shasum -a 256 -c WeavePort-0.1.0-internal.2-osx-arm64.tar.gz.sha256
tar -xzf WeavePort-0.1.0-internal.2-osx-arm64.tar.gz
cd WeavePort-0.1.0-internal.2-osx-arm64
python3 -B weaveport.py install "$HOME/Applications/WeavePort/0.1.0-internal.2"
```

Use `--dotnet /absolute/path/to/dotnet --python /absolute/path/to/python3` if needed. A mismatch is refused before creating the destination. Existing destinations, even empty ones or incomplete installs, are never replaced.

The installed commands work from any working directory:

```sh
python3 -B "$HOME/Applications/WeavePort/0.1.0-internal.2/weaveport.py" doctor
python3 -B "$HOME/Applications/WeavePort/0.1.0-internal.2/weaveport.py" run decision-room -- --verify
python3 -B "$HOME/Applications/WeavePort/0.1.0-internal.2/weaveport.py" run document-workshop -- --verify
python3 -B "$HOME/Applications/WeavePort/0.1.0-internal.2/weaveport.py" run appointment-desk -- --verify
```

Omit `--verify` for a normal example run. Decision Room demonstrates shared C#/Python decision strategies and journal replay; Document Workshop demonstrates interchangeable document readers; Appointment Desk demonstrates booking strategies and recovery of uncertain effects. Application options after `--` are passed through. Supply absolute paths for custom files, configuration and stores: the launcher uses the bundled template directory for default fixtures.

## Installed layout and state

- `payload/packages/nuget`: four exact core packages plus the two reviewed Microsoft logging dependencies, usable as an offline local NuGet feed.
- `payload/packages/python` and `payload/packages/typescript`: the qualified author SDK packages.
- `payload/templates`: copyable source trees for all three applications and shared coordinator/restart templates, root build settings and a self-contained local feed.
- `payload/evidence` and `payload/distribution.json`: retained qualification and delivery checksums.
- `var/<application>`: executable copies, selectors and generated journals, documents, calendar data, verification evidence and worker state.
- `installation.json`: completion receipt and local runtime paths. It is written only after successful installation.

Diagnostics check shipped content, executable copies and required runtimes without resetting state. The `var/` application data is deliberately excluded from the shipped-content inventory. Back up the whole `var/` directory before manual maintenance. Appointment Desk's unresolved run marker continues to block unsafe restart; see `payload/docs/native-operations.md` for recovery boundaries. Doctor passing does not mean an application has no outstanding recovery work.

Do not move an installed directory: the Python environment and receipt bind it to its destination. Install future deliveries side by side. No automatic update, migration, uninstall, service registration or global PATH changes are included. Failed setup leaves an incomplete directory for inspection, which cannot launch normally. Remove it manually only after establishing that it contains no state to retain, then retry. Retain used installations for rollback; never delete recovery evidence to force a restart.

## Use as an integration template

Copy the **entire** `payload/templates` directory to a writable directory outside the installation, keeping its relative structure and bundled `artifacts/packages` feed. Build, for example:

```sh
dotnet build samples/DecisionRoom/Host -c Release
dotnet build samples/DecisionRoom/Plugin -c Release
dotnet build samples/DocumentWorkshop/Host -c Release
dotnet build samples/DocumentWorkshop/Worker -c Release
dotnet build samples/AppointmentDesk/Host -c Release
dotnet build samples/AppointmentDesk/Worker -c Release
```

These are application sources, not a generator that requalifies changed plugins. Original sample READMEs describe repository development scripts; those scripts are not part of the copied template. For qualified execution use the installed launcher. Changes to plugin builds require new installation manifests and qualification; never silently reseal a persisted operation's pinned installation.

For an existing application's integration, add `payload/packages/nuget` as an explicit local source and use a fresh package cache. Do not mix earlier packages sharing `0.1.0-internal.2` with this feed. The three production applications remain unchanged. Optional Composition, Gateway and Testing packages are outside this delivery's package set.

## Reproduce packaging

From the WeavePort repository:

```sh
python3 -B tools/distribution/package.py /absolute/path/to/retained-candidate --output artifacts/internal-distribution
python3 -B tools/distribution/verify.py artifacts/internal-distribution/WeavePort-0.1.0-internal.2-osx-arm64.tar.gz
```

The packager verifies candidate bytes against checked-in qualification evidence, reads template sources from the qualified Git commit (including separately recorded qualification lockfile refreshes), and includes current distribution tooling and this guide. Output directories are never reused. Archive checksums identify an individual delivery; bit-identical archives across independent builds are not claimed.
