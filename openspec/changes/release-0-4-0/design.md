# Design

The release is explicitly authorized by the user, conditional on audit and successful qualification. Preserve default customer-bound execution and the operator-approved cooperative sharing contract. Use 0.4.0 for the additive public feature set, and 0.2.0 for the new Python/TypeScript author APIs. Do not reuse old package identities for different SDK behavior.

Audit includes exact deployment/version matching, single-flight ownership, revocation before cleanup, failure/cancellation retirement, stream pinning, old-binding disposal, global accounting, fair admission, explicit restart, public API preservation and documentation/benchmark limits. A deliberately retained global remains an accepted negative control, not a release blocker hidden by successful tests.

The existing release allowlist remains the four core NuGet packages. Python/TypeScript registry publication is not configured; distribute the exact qualified wheel/tarball as release assets and document installation from those assets. Do not invent registry credentials or weaken existing publication gates.

Fix the missing clean-candidate reuse stage. Capture its executable/provider files in the frozen artifact inventory, compare packed/source Hosting and SDK DLL identities, and run the maintained author examples through alternating customers. Historical benchmark hashes remain historical; they are not renamed to release measurements.


Audit found a scheduler completion handoff race: the completion task was signalled before its active/idle flags were updated. An awaiting caller could observe completed work still marked active and receive an unnecessary restart rejection. Complete the public task only after releasing that scheduler activity under the same lock; assert the public idle snapshot after every sequential test call.
