## Why

WeavePort has maintained repository guides but no coherent public documentation website or visual identity. Developers need a clear entry point at weaveport.dev that explains the current release and leads to runnable examples.

## What Changes

- Add a DocFX website with a branded landing page, getting started, conceptual introduction, grouped canonical guides, samples and a reviewed API reference.
- Add an original WeavePort logo visually related to Stratara.
- Generate website pages from existing guidance; keep product requirements in OpenSpec.
- Add a reproducible build, generated-site verification and deployment instructions for static hosting.

## Capabilities

Documentation and build tooling only (`skip_specs: true`). No runtime contract or release change.

## Impact

New website sources and assets, build tooling, CI documentation checks and documentation entry links. Existing canonical guides retain their locations. Website publication is separate from package publication.
