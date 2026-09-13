# Quality distribution verification — 2026-09-12

`WeavePort-0.1.0-internal.2-osx-arm64.tar.gz` passed independent extraction, installation and execution outside the checkout. SHA-256: `6370aa8ec620528de3d399b70f2aa444779c387b7aeb960ecd56dbd4f79f531c`.

The archive contains 189 inventoried files and 102 candidate-qualified artifacts. All shipped Markdown relative links resolve; copied templates include their own guidance closure. Repository-only context uses explicit qualified-commit links. Each WeavePort NuGet readme was checked inside its archive. The four core packages and two MIT-licensed Microsoft dependencies restore completely offline.

The standalone installation passed all 144 application assertions and normal runs of all three examples. Six copied consumer projects built using only the included feed; restored package hashes matched the delivered manifest. Wrong runtimes, damaged/extra bundle content, incomplete receipts, changed installed executables and used destinations were refused. Reinstallation and doctor preserved generated state. The final doctor passed after independent template builds.

A side-by-side installation at the owner's existing WeavePort application location also passed doctor. The previous internal.1 installation and its archive were preserved.

The additional real HTTP/2 gateway regression passed on .NET 10.0.12: 32 parallel calls, queued cancellation, idle transport reuse, error observation, credential revocation and an unaffected binding. Gateway remains outside the offline core distribution. Its source is the same reviewed implementation; its separate regression result is retained here.

During verification-tool refactoring, the first standalone run exposed a missing local launcher reference after all six consumer builds. The verifier was corrected and the entire standalone verification was rerun successfully against the unchanged archive. Failed verification now records a failed status explicitly. This tooling correction does not change the qualified runtime artifacts.
