## Why
Installed consumers still lose startup version diagnostics and per-call timing, and discovery silently hides unselected plugin directories. Complete the consumer findings before qualifying the next release.

## What Changes
- Add explicit assembly-derived author version selection and structured startup version mismatch details.
- Add diagnostic catalog enumeration with one result or refusal per plugin directory.
- Return per-call elapsed metadata alongside typed values locally and across the gateway, preserving legacy clients.
- Review documentation and executable examples, audit the complete change, and qualify a versioned release.

## Capabilities
### Modified Capabilities
- `plugin-sdk`: version helpers and per-call metadata.
- `installed-plugin-resolution`: complete diagnostic discovery.

## Impact
Abstractions, Hosting, author SDK, local and gateway clients/server, public API and wire baselines, package consumers and documentation. Existing version default, List behavior, ownership, cancellation and uncertain outcomes remain compatible. Domain metadata and shape validation remain consumer-owned.
