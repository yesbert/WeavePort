## Decisions
Rename outer internal signature helpers instead of serialized envelope properties to retain exact wire names and source-generated metadata. Select runtime dictionary keys without unused values, remove the unused root parameter, and use equivalent default string equality through ContainsValue. Cache test-only reflection serializer options while retaining their explicit resolver and existing byte-equivalence assertions.

SDK 10.0.403, net10.0, C# 14. Microsoft Learn checked on 2026-10-30:
- https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1841
- https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1869
- https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2.containsvalue?view=net-10.0

## Evidence and limits
Main analysis run 41022457954 reports S1172, S1481, CA1841, two S3218 findings and CA1869. Coverage is 82.2% on new code and duplication 0%; neither causes the failure. No suppressions or relaxed gate thresholds are used. Existing hosting, reflection-disabled sealing, publisher interoperability and style checks verify the correction. Full committed qualification runs in PR CI and again on the release tag.
