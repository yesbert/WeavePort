## Why

Consumers cannot restore the implemented optional functionality from NuGet.org. Publish the existing bounded composition library with a reviewed API, reliable cleanup, local/remote SDK integration and packed consumer qualification.

## What Changes

- Publish the existing bounded composition library with a reviewed API, reliable cleanup, local/remote SDK integration and packed consumer qualification.
- Include the new packages in the frozen release candidate, API review and NuGet export with package-specific documentation.
- Coordinate a new 0.4.0 package family and exact core compatibility inputs; retain Testing as internal.
- **BREAKING** for previously source-built optional consumers: gateway client assembly placement and explicit server binding identity change. Existing published core interfaces remain usable.
- Non-goals: distributed scheduling, durable composition recovery, arbitrary proxy/cloud topology qualification, automatic retries and hostile-plugin containment.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `bulk-composition`: publicly consumable optional packages with tested contracts and explicit deployment limits.

## Impact

Optional and SDK client source, package consumers, compatibility snapshots, release candidate tooling, release documentation and NuGet allowlist. Composition consumes the common SDK client contract without referencing gateway implementations. Gateway server references the separate client package; client applications do not require ASP.NET Core.
