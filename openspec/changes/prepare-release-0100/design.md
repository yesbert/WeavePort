## Decisions
Use minor version 0.10.0 for the additive function file API. Raise the Python and TypeScript author SDKs to 0.5.0 because they gain public file APIs and advertise the new capability. Keep host API 2 and native protocols 1/2: the capability is an additive ready field and invoke extension that old and new peers negotiate per call.

Regenerate the independent OpenSSL publisher fixture with a temporary, discarded private key, because the compatibility matrix is part of the signed bytes. Refresh WeavePort entries in consumer lockfiles from locally packed 0.10.0 packages; external dependencies stay unchanged. Retain historical reports and migration sections, and distinguish the prepared 0.10.0 from the published 0.9.1.

## Alternatives and limits
A patch release would hide an additive public API and new SDK behavior. This preparation adds no platform or performance guarantees and no sandboxing.
