## Decisions
Use minor release 0.8.0 because diagnostic APIs are additive and failure/cancellation interpretation changes from 0.7.0. Author SDKs move from 0.3.0 to 0.4.0 so modified artifacts never reuse a published identity. Keep host API 2 and native protocols 1/2. Exact installation policy requires offline resealing and deliberate pin migration, not in-place rewriting.

Update maintained consumer guidance and generated retrieval files together. Preserve historical reports and audit ledgers. Qualify committed source using the existing clean-candidate pipeline, then repeat qualification on the immutable annotated tag. Publish via the existing protected NuGet workflow and compare public payloads with the original tested artifacts.

## Alternatives and limits
A patch obscures new API features and changed failure behavior. Reusing author SDK 0.3.0 would give different bytes the same published version. Mixed-family compatibility, automatic migration, new performance claims and PyPI/npm registry publication are not included.
