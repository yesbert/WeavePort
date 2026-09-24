## Why

The main-only static analyzer retained findings outside the passing PR checks. Finish the existing readability audit by resolving remaining exception-cleanup, failure-mapping and optional-wait ambiguity without changing public contracts.

## What Changes

- Preserve execution and cleanup failures without explicitly throwing from finally.
- Express optional shared-startup waiting with a Try method, and name failure construction separately.
- Preserve instance exception-code APIs while removing misleading computed-constant accessors.
- Add source and packed regression evidence for cleanup cause and stack preservation; verify the main-only quality gate.

## Capabilities

No new product requirement. Existing structured-failure and worker-lifecycle contracts apply (`skip_specs: true`).

## Impact

SDK session cleanup, host error construction and shared warmup selection; verification tooling and audit evidence. No version or release change.
