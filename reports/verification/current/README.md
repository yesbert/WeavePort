# Reduced repository qualification — 2026-09-13

Source: `148d13abd177a2a944d382f8fc5ff971a2cf9d97` (resolve the full identity in [result.json](result.json)). The normal qualification command created a fresh detached checkout with fresh dependency caches. All **399 assertion executions** passed, including source/packed contracts, gateway regressions, the four documentation checks, application/SDK/recovery/compatibility scenarios and the changed-package negative control. The code-style gate passed. **135 frozen artifacts** remained unchanged; [their hashes](artifact-manifest.json) identify this maintenance qualification, not a new released candidate.

Additional checks of the moved fixtures passed at source `59d8168` (their functional code is unchanged): **43 native adapter assertions** through the moved `WeavePort.Local.Tests` fixture, and **24 Composition assertions** through the moved `WeavePort.Composition.Tests` fixture. Docker execution was not rerun; its service was not interrupted or reconfigured.

The relocated release evidence also passed the distribution packager's exact-artifact checks. Its temporary test archive then passed standalone installation, **144 application assertions**, state-preservation checks and **six copied-template builds**. That packaging regression archive is not a new published delivery. The existing installed application and original qualified archive were not replaced.

[Current performance evidence](../../benchmarks/current/README.md) independently measures the exact delivered `0.1.0-internal.2` core and author SDK bytes, with optional Gateway identified separately. The maintenance build above is deliberately not substituted for that delivery in benchmark claims.

Scope remains native macOS arm64; Windows qualification remains open. Raw current run logs and temporary installation evidence are retained locally, outside the maintained report tree.

The complete 37-project Release solution builds with zero warnings/errors. The security fixture additionally restores with its committed lockfile into an empty cache using only the prepared offline feed. The final fresh-checkout result above includes the isolated adapter-feed configuration; no root feed changes are required.
