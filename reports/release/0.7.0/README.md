# WeavePort 0.7.0

Release preparation for seven coherent .NET packages at 0.7.0, with Python and TypeScript author SDK artifacts at 0.3.0. The [audit](audit.md) covers portable installations, the original consumer findings and documentation/example consistency.

## Delivery scope

- Public .NET sealing with embedded exact compatibility policy and portable ecosystem runtime requirements.
- Complete catalog diagnostics, explicit assembly-derived plugin versions and actionable startup mismatch details.
- Typed and JSON per-call host timing locally and through optional gateway protocol fields.
- Updated documentation, runnable examples, public API baselines and package references.

Exact package migration remains deliberate: resealing changes the installation identity and never rewrites existing recovery pins. The default author version remains `1`. Version mismatch now reports `version-mismatch`, not generic `protocol-error`, before dispatch.

## Qualification and publication

Final qualification and publication evidence will be added after successful execution. A source version bump alone does not establish publication. Historical portable-runtime evidence is retained [separately](../../verification/portable-runtime-20260923/summary.md).
