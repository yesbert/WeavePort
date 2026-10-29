## Decisions
Use minor version 0.9.0 for the additive trusted publisher API and accompanying JSON fix. Keep author SDK 0.4.0, host API 2 and native protocols 1/2 because their sources/contracts do not change. Require offline rebuild/reseal, re-signing and deliberate pin migration for the exact package matrix.

Regenerate the independent OpenSSL fixture using a temporary discarded private key: changing the compatibility matrix changes signed bytes. Retain historical reports and migration sections. Clearly distinguish prepared 0.9.0 from published 0.8.0.

Qualify committed source in a fresh checkout and export the original tested packages. Publication will require separate authorization and exact-tag qualification through the protected workflow.

## Alternatives and limits
A patch hides the additive security API; republishing 0.8.0 would reuse an immutable identity. This preparation does not add platform/performance guarantees, sandboxing, rollback protection or immediate revocation of existing handles.
