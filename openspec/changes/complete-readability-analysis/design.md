## Decisions

The archived exhaustive audit remains an exact historical record. This follow-up records findings from the main-only SonarQube analysis separately; prior failed analysis is not silently relabelled as green. Preserve all public signatures and wire statuses.

Cleanup must still run through finally, including when execution raises an excluded fatal error. Capture ordinary cleanup failures there, then aggregate or rethrow after leaving finally. ExceptionDispatchInfo preserves original stack information. Fatal errors are not converted into ordinary plugin failures; a secondary ordinary cleanup failure must not hide an OutOfMemoryException. Explicit cause order remains execution first, cleanup second.

An optional warmup is a scheduling decision, so return a boolean plus a pending task instead of a nullable Task-returning method. Keep selection under the same pool lock and preserve the first-completed-startup wait. Instance error codes remain instance properties to preserve caller APIs; initialized getters express that contract without a misleading stateless computed member.

## Reference checks

SDK 10.0.401, net10.0, C# 14.0 rechecked. Current Microsoft guidance: [CA2219](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2219), [ExceptionDispatchInfo.Throw](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo.throw?view=net-10.0). Do not suppress the findings or change analyzer thresholds to pass.

## Verification

Add direct checks against the actually loaded SDK assembly for success, primary failure, cleanup failure, combined failure, cancellation, original stack and fatal-error preservation. Run them for source and frozen package consumers alongside worker/scheduler regressions. Public baselines remain unchanged; final main-only analysis is required before declaring this follow-up complete.

The first main analysis after PR #41 reports 16 findings (no security hotspots, 84.3% new-code coverage, zero duplication). Besides the remaining cleanup/wait/code-property findings, it identifies manifest resolution complexity, repeatedly constructed fixture arrays and report serializer options. Manifest declaration validation is extracted before the same compatibility/inventory checks; independent fixture values remain literal and private static arrays, and report serialization options are reused. No rule is suppressed.

The nine cleanup assertions also run in the Linux coverage collection, not only the functional candidate. The expected executable-suite count derives from the explicit concurrent mode catalogue, preserving failure detection when a suite is omitted.
