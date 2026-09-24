# Documentation website

The design follows a focused product story, concrete examples and grouped documentation navigation, informed by Blume and other open-source projects. The static website targets `https://weaveport.dev` and follows Stratara's DocFX documentation approach. The maintained inputs are the English pages in this directory, canonical guides under `docs/`, the three application sample readmes, runnable example and SDK guides, contributor/verification guidance, both reviewed API baselines and the generated AI retrieval documents.

## Build and preview

Use the .NET SDK selected by `global.json` and Python 3. The local tool manifest pins DocFX 2.78.5; restoring it needs access to NuGet.

```sh
dotnet tool restore
python3 scripts/build-website.py
python3 -m http.server 8080 --bind 127.0.0.1 --directory artifacts/documentation/site
```

Open `http://127.0.0.1:8080`. The build recreates only its own ignored staging/output directories, fails on DocFX warnings, and validates local HTML links, assets, anchors, the search index and the sitemap. Theme assets and search are self-hosted. No analytics or external font service is configured.

## Content ownership

Edit canonical guides in their original locations. `scripts/build-website.py` maps guides and adoption pages into `docs/` and sample readmes into `docs/examples/`. Repository-only links are rewritten to GitHub. The build never treats unfinished OpenSpec changes as implemented behavior. Additional top-level site pages belong in this directory; add navigation entries to `GROUPS` in the build script.

The core and optional public API pages come from `compatibility/public-api.txt` and `compatibility/optional-api.txt`; both are current-checkout signature references, not newly generated contracts or claims about original published packages. Keep post-release source availability explicit. The same build exports both raw baselines for retrieval. Every build generates website AI retrieval files and Markdown pages from the same canonical sources. The website index links to same-release Markdown on weaveport.dev; repository copies retain GitHub URLs. HTML discovery links expose the Markdown counterpart and llms.txt. CI rejects stale repository copies, and the website checker validates retrieval targets. Deploy the complete artifact to keep human and AI documentation synchronized.

## Branding

`assets/logo.png` is the original WeavePort logo, generated using the built-in image-generation tool with the Stratara logo as a visual style reference. The geometric blue planes form an interwoven W and portal. The exact prompt is retained in `assets/logo-prompt.txt`. The source image is preserved at its generated resolution and transparency.

## Static deployment

1. Run the full website build and repository documentation checks.
2. Archive the **contents** of `artifacts/documentation/site` as a versioned release. Retain its SHA-256 checksum and source commit alongside it; record whether the source tree contains uncommitted changes.
3. Transfer the archive to a separate staging/release directory on the target host and verify the checksum there.
4. Configure the dedicated `weaveport.dev` virtual host and valid HTTPS certificate using the existing hosting control plane. Confirm the operator/contact and hosting policy for this domain. Never reuse another site's document root.
5. Extract to a new release directory, verify the entry page and assets, then point the dedicated document root at that release. Retain the previous release and document-root target for rollback.
6. Check HTTPS, a nested guide, logo, search index, sitemap and both AI files through the public origin. Confirm HTTP redirects to HTTPS.

Rollback restores the previous document-root target through the same hosting control plane. No runtime, NuGet publication, Docker changes or modification of sibling websites is needed. The repository contains no credentials or server-specific deployment paths. The [automatic CI deployment](../docs/continuous-integration.md) publishes a validated main-run artifact through a dedicated restricted SSH account.

DocFX references: [configuration](https://dotnet.github.io/docfx/docs/config.html) and [modern template customization](https://dotnet.github.io/docfx/docs/template.html).
