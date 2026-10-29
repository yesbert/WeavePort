# WeavePort 0.9.0 preparation

Status: prepared version allocation; qualification in progress. Not tagged or published. The latest published release remains [0.8.0](../0.8.0/README.md).

This release includes the reflection-free installation JSON fix and opt-in trusted publisher signatures merged in [PR #47](https://github.com/yesbert/WeavePort/pull/47). All seven .NET packages use 0.9.0; Python/TypeScript SDKs remain 0.4.0, host API 2 and native protocols 1/2 remain unchanged.

See [release and migration notes](../../../docs/releases.md#090-trusted-publishers-and-reflection-free-installation-json) for offline resealing, signing, pin migration and trust-policy limits. Existing integrity-only constructors remain available. No new dependency, sandboxing, platform qualification or performance promise is included.

Publication is outside this preparation. After review and merge, separately authorize tagging and the protected release workflow; retain exact-tag qualification and verify public downloads before announcing availability.
