## Decisions
Use minor release 0.8.0 because diagnostic APIs are additive and failure/cancellation interpretation changes from 0.7.0. Author SDKs move from 0.3.0 to 0.4.0 so modified artifacts never reuse a published identity. Keep host API 2 and native protocols 1/2. Exact installation policy requires offline resealing and deliberate pin migration, not in-place rewriting.

Update maintained consumer guidance and generated retrieval files together. Preserve historical reports and audit ledgers. Qualify committed source using the existing clean-candidate pipeline, then repeat qualification on the immutable annotated tag. Publish via the existing protected NuGet workflow and compare public payloads with the original tested artifacts.

## Alternatives and limits
A patch obscures new API features and changed failure behavior. Reusing author SDK 0.3.0 would give different bytes the same published version. Mixed-family compatibility, automatic migration, new performance claims and PyPI/npm registry publication are not included.

## Prepublication failure and correction
The first tag attempt was cancelled before publication after the additional main CI exposed a gateway cancellation/disposal race. An owned cancellation callback can dispose a gRPC stream before its next read/write starts. Convert `ObjectDisposedException` to token-associated `OperationCanceledException` only when that operation token is requested; preserve unrelated disposal unchanged. Deterministic boundary fixtures and resumed real streams/sources exercise both cases without timing retries or relaxed deadlines. Remove the unpublished tag and recreate it only on the corrected reviewed revision; never move a published release tag.

References checked against .NET 10 / SDK 10.0.401:
- https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
- https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0
