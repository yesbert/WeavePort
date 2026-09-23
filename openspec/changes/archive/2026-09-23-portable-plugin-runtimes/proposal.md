## Why

Installation manifests currently bind plugins to the exact runtime executable used when sealing, preventing an otherwise portable plugin bundle from moving between development machines and container hosts. Consumers also have to reproduce a Python build script and copy compatibility metadata to seal installations from a .NET-only repository.

## What Changes

- Introduce a versioned portable manifest that derives runtime requirements from `.runtimeconfig.json`, `pyproject.toml` and `package.json`, retaining ecosystem version semantics.
- Validate the operator-selected local runtime against those requirements before launch, with diagnostics identifying the requirement and observed runtime. Keep an optional executable hash for stricter deployments.
- Preserve complete plugin file hashing, exact manifest pins, operator-approved runtime aliases and package/API/protocol/SDK compatibility validation.
- Add a public .NET sealing API using the packaged compatibility matrix, without requiring Python, a repository checkout or an installed target runtime for portable sealing.
- Keep existing schema-1 manifests under their existing strict hash rules. Migration is explicit resealing of an offline release, never automatic mutation of an active installation or persisted pin.
- Update first-party sealing entry points, documentation and packed consumer verification to exercise portable installations.

Non-goals: plugin-card metadata, discovery diagnostics, invocation timing APIs, automatic plugin-version inference, startup plugin-version diagnostics, runtime installation/downloads, hostile-code isolation, dependency restoration, and release publication. Portable runtime selection does not make native dependencies portable.

## Capabilities

### New Capabilities

- `plugin-installation-sealing`: Create validated, deterministic installation manifests from a built release through a public .NET API and embedded compatibility policy.

### Modified Capabilities

- `installed-plugin-resolution`: Resolve portable runtime declarations while retaining strict legacy manifests, bundle integrity, operator approval and exact recovery identities.

## Impact

Changes affect `WeavePort.Hosting` catalog, launch validation and runtime probing; its public API baseline; `scripts/seal-installation.py` and first-party build callers; installation documentation; and packed installation consumer tests. Runtime probes introduce bounded subprocess work at resolution/binding boundaries, not per invocation. TOML and ecosystem version parsing dependencies require license and packaging review before adoption. Existing exact package compatibility remains unchanged. Older hosts will reject the new manifest schema; consumers must upgrade before opting in. No existing source is retired by this proposal.
