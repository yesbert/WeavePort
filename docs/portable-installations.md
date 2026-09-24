# Portable installed plugins

Version 0.7.0 introduced portable schema-2 installations and a .NET sealing API, retained in 0.8.0. Use one coherent package family and its exact embedded compatibility matrix. Existing schema-1 manifests retain their executable hash checks, exact package compatibility and recovery pins.

## Seal an offline release

Use the Hosting package to seal built files without a WeavePort checkout, a copied compatibility matrix or Python. Portable sealing does not require the target runtime to be installed and does not execute plugin code, restore dependencies or run build backends.

```csharp
InstallationIdentity identity = PluginInstallationSealer.Seal(new PluginSealOptions
{
    ReleaseDirectory = "/deployment/plugins/example/releases/1.0.0",
    Plugin = "example",
    Version = "1.0.0",
    Contract = "example/v1",
    EntryPoints = new Dictionary<string, string> { ["dotnet"] = "Example.dll" },
    Launch = new PluginLaunchDeclaration { Runtime = "dotnet" }
});
```

The sealer hashes the complete inventory except `installation.json`, validates launch and ecosystem declarations and atomically replaces the manifest after validation. It does not delete bytecode caches, change an active selector or update persisted pins. Identical bytes and options produce identical manifests across release paths. Keep the release stable throughout sealing and execution.

`RuntimeSources` optionally maps entry aliases to relative declaration paths; defaults are the entry assembly's adjacent `.runtimeconfig.json`, root `pyproject.toml` or root `package.json`. `StrictRuntimeFiles` maps selected aliases to local executables whose SHA-256 must additionally match at resolution and every worker start. `ExternalFiles` records separate hashes for explicitly external SDK/code files. Supply those aliases again in the catalog's approved local file mapping. Including SDK code directly in the bundle is also supported.

## Resolve on the target host

```csharp
var catalog = new InstalledPluginCatalog("/deployment/plugins",
    new Dictionary<string, string> { ["dotnet"] = "/usr/share/dotnet/dotnet" });
InstalledPlugin installed = await catalog.ResolveAsync(
    "example", "1.0.0", "example/v1", pinned: identity,
    cancellationToken: cancellationToken);
```

`ListAsync` and `ActivateAsync` provide cancellable equivalents of the existing synchronous catalog operations. Initial and replacement installed workers check compatibility before launching. No runtime is downloaded or chosen through PATH. An incompatibility reports the runtime alias, required constraint and observed inventory/version; probe failures report why the observation failed. A startup failure encountered through a running client's automatic replacement retains the existing invocation failure mapping.

The host probes only approved executables, from the approved executable’s directory with a cleared environment, fixed version-query arguments, a five-second process deadline and a combined 64-Ki-character output ceiling. Probe cleanup and synchronous reader drain have additional bounded waits. Cancellation stops asynchronous probes. No probe runs per invocation on an already running worker. Worker processes also use the existing cleared launch environment, so inherited `DOTNET_ROLL_FORWARD`, `NODE_OPTIONS` and Python startup variables cannot weaken validation. Portable .NET launch arguments cannot override sealed framework selection.

## Runtime requirements and limits

| Ecosystem | Declaration | Supported behavior |
|---|---|---|
| .NET | Adjacent `runtimeOptions` in `.runtimeconfig.json` | Stable framework-dependent `framework`/`frameworks`, minimum versions, all six `rollForward` policies and framework dependencies from the selected runtime installation |
| Python | Static `[project].requires-python` in `pyproject.toml` | Python packaging version/specifier semantics, including exclusions, compatible releases, prefixes and prerelease constraints |
| Node | `engines.node` in `package.json` | npm ranges, including comparator sets, disjunctions, caret, tilde, wildcards and prerelease opt-in |

Missing or malformed declarations fail closed. Dynamic-only Python requirements, .NET prerelease framework declarations, legacy `applyPatches`/`rollForwardOnNoCandidateFx` settings, self-contained .NET and Native AOT are outside portable schema-2 support. They do not silently become unrestricted. The .NET SDK version is not evidence that a required shared framework is installed. Python specifiers and npm ranges are not interchangeable. WeavePort enforces Node's engine declaration even where npm would only warn.

Compatibility does not guarantee identical application behavior after an update, platform portability of native dependencies, or runtime integrity. Integrity of the deployed runtime and its dependency closure belongs to the deployment. Optional executable hashes verify that executable only. Hash verification does not provide a sandbox or prevent files changing between checking and execution. See [installation trust boundaries](installed-plugins.md).

## Migration and verification

Upgrade the host before using schema 2. Explicitly reseal an offline release to create a new identity; existing pins are never rewritten. Retain old releases and their runtime deployment for rollback. Changes to a manifest's exact bytes still invalidate its old pin, while unchanged portable manifests can be recovered under a compatible runtime on another supported host.

The [verification report](../reports/verification/portable-runtime-20260923/summary.md) distinguishes executed macOS/Linux container cases from parser fixtures and lists the measured startup scope. Run `scripts/verify-portable.sh` for packed consumers and language/startup checks, and `python3 tests/portable/verify-frameworks.py "$(command -v dotnet)"` for real .NET selection comparisons. Docker portability verification is an explicit optional argument to the former script; it does not stop or reconfigure existing services.
