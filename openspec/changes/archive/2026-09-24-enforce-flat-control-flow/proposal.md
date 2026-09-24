## Why

The source-organization change did not enforce the existing preference for guard clauses. Nested decision and iteration logic remains difficult to review, and exception construction can be mistaken for automatic global logging.

## What Changes

- Make the control-flow convention explicit and executable across maintained C# and multilingual SDK code.
- Flatten nested branches and loops with early exits and named operations while preserving resource ownership, cancellation, dispatch and error semantics.
- Document who logs each failure category and distinguish diagnostic messages, stable error codes and event IDs.
- Add negative fixtures for the quality gates and requalify the refactored implementation.

## Capabilities

No observable runtime requirement changes. This is a behavior-preserving refactor, coding-policy clarification and verification improvement (`skip_specs: true`). No global exception hooks or raw exception logging are introduced.

## Impact

Core packages, maintained application templates, author SDKs, engineering/diagnostic guidance and verification tooling. Existing public signatures, wire values and safe diagnostic boundaries remain unchanged.
