## Why

Published packages omit the existing project logo. NuGet versions are immutable, so corrected branding requires a patch release.

## What Changes

Embed the unchanged website PNG through shared package metadata, verify its identity during release export, and publish the four existing core packages as 0.2.1 with synchronized exact compatibility inputs.

## Capabilities

No runtime behavior changes.

## Impact

NuGet metadata, release tooling, current version references and release evidence.
