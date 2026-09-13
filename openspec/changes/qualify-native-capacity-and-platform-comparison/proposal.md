> **Status:** partially implemented; Windows qualification remains open.

## Why

Platform support and capacity claims require measured execution under a recorded topology, not inference from compilation or experiments on a different operating system.

## What Changes

- Record explicit capacity measurement controls and runtime/topology identities.
- Compare native process and container deployments with matched workloads.
- Complete native Windows functional and performance qualification when an appropriate environment is available.

## Capabilities

Measurement tooling and evidence under existing performance-evidence requirements (`skip_specs: true`). No product runtime contract changes.

## Impact

Benchmark tooling and platform documentation. Earlier completed experiment records predate the public baseline. The remaining work is documented in docs/platform-qualification.md. Unrelated services must not be interrupted by qualification runs.
