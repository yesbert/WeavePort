## Why

The verified GitHub SonarQube report for e9a0f491e487b5729975618077d3bfeddf13bdb7 contains 37 issues and two unreviewed security hotspots. Correct resource lifetime defects, simplify validation and resolve unsafe executable/temp-directory selection without weakening analysis.

## What Changes

- Dispose host/session/pool/client cancellation sources safely, preserving concurrent shutdown and detached callbacks.
- Use exclusive private socket directory creation and explicit absolute Docker executable resolution.
- Refactor complex validation and stream code, preserve manifest compatibility deserialization, improve diagnostics and internal conventions.
- Migrate the development API to the plural ExecutionProtections enum and final cancellation-token arguments; update all consumers and the reviewed API baseline.

## Capabilities

### Modified Capabilities

- `worker-lifecycle`: safe resource completion and adapter setup.
- `plugin-sdk`: safe local client shutdown.

## Impact

Hosting, Composition, SDK Client/Gateway, focused regression suites and deployment guidance. No new package dependencies, Docker service changes, or performance guarantees.
