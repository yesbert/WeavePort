## Why

Flat source directories, mixed public entry points and runtime implementations, and scattered package versions make ownership and changes difficult to review. Diagnostics need a discoverable stable vocabulary rather than dependence on exception messages.

## What Changes

- Centralize NuGet versions without upgrading dependencies or changing package boundaries.
- Organize C# by cohesive execution, installation, protocol, scheduling and diagnostics responsibilities; separate public contract types.
- Separate TypeScript and Python public entry points, registration, session context and runtime implementation, and expand compressed control flow.
- Preserve generated logging event IDs and document their names and safe fields.
- Add stable programmatic error codes to platform-specific exceptions while preserving standard exception categories and wire statuses.
- Add structural checks and focused regression evidence, and document navigation and extension rules.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `runtime-diagnostics`: discoverable stable codes for platform-specific exceptions alongside existing operational events.

## Impact

Core source packages, multilingual author SDKs, package configuration, diagnostic documentation, verification tooling and their tests. Public namespaces/imports, execution ordering, cancellation, cleanup, wire values and dependency versions remain compatible. No package publication, dependency upgrades, new infrastructure, performance claims or platform qualification.
