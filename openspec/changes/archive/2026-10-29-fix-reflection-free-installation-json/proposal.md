## Why

A consumer disabling System.Text.Json reflection cannot seal a valid .NET release: framework requirement serialization throws and is reported as an invalid runtime declaration. The embedded matrix, manifest and resolution paths also rely on implicit reflection.

## What Changes

- Use generated JSON metadata throughout installation sealing and resolution without changing manifest formatting, hashes or public APIs.
- Verify the complete path in a consumer with JSON reflection disabled, including offline Python/Node sealing and failed-input preservation.
- Non-goals: general Hosting Native AOT qualification, plugin payload serialization changes or publisher signing.

## Capabilities

### New Capabilities
None.

### Modified Capabilities
- `plugin-installation-sealing`: Add a concrete reflection-disabled consumer scenario to package-contained sealing.

## Impact

Hosting installation serialization, focused source and packed-consumer checks, and verification entry points. No new package dependency or wire-format change.
