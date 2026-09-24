## Why
Failure classification currently loses SDK codes and cleanup causes, while transport descriptions can become programmatic codes. Repeated protocol literals make cross-language maintenance error-prone.

## What Changes
- Preserve safe failure metadata and primary/cleanup causes, classify cancellation by its actual owner, and separate protocol from internal failures.
- Map gRPC status categories to stable codes and carry platform codes separately.
- Add explicitly opted-in detailed diagnostics while retaining safe default logging and trace outcomes.
- Generate shared protocol vocabulary and limits from one reviewed contract, with drift checks and independent wire fixtures.
- Replace meaningful implementation literals with component-owned policy names and improve TypeScript message typing.

## Capabilities
### New Capabilities
- `structured-failures`: Safe structured errors, cancellation classification and transport mappings.
### Modified Capabilities
- `runtime-diagnostics`: Correlated safe failure events and opt-in exception details.

## Impact
Host, client, gateway and all three author SDKs; additive public metadata and wire fields, engineering documentation and candidate gates. Existing wire identifiers and limits remain unchanged. No new runtime dependencies, automatic retries or changes to isolation guarantees.
