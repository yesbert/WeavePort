## Decisions

Use DocFX 2.78.5 and its modern template, following the existing Stratara documentation structure. Pin the tool locally for reproducible installation. Build static files without a server-side application or new runtime dependency.

Keep existing docs and sample readmes as the canonical sources. A staging generator maps these into website paths and rewrites repository-only links to GitHub. Generated pages live under ignored artifacts; they are never edited or committed. Build failures and broken local output links prevent accepting the website artifact.

Use an original geometric blue W/portal logo and original layout styling. Stratara supplies a visual reference; its product claims, analytics and consent scripts are not copied. Self-host static assets. Search executes in the browser.

Expose the reviewed public API signature inventory as a reference page. Do not regenerate a competing API contract or claim experimental packages are publicly released. Keep public 0.1.0, historical internal distribution and unqualified platform/deployment combinations explicit.

A custom site generator was rejected because DocFX already provides navigation, search, syntax highlighting and theme support consistent with Stratara. Duplicating existing guides by hand was rejected because it creates drift.

## Verification and publication boundary

Build with warnings treated as errors, check generated local links/assets/anchors, search index and sitemap, and inspect desktop/mobile rendering. Run existing documentation, public-tree and OpenSpec checks. This does not requalify the runtime or publish NuGet packages.

Deployment uses a versioned static artifact and a separate activation step with rollback. Domain hosting and HTTPS must be configured before public activation; do not overwrite another site's document root. Server identities and access details stay in private deployment notes.

## Outcome

The website is built and verified, with 35 content pages, a branded landing page, full-text search and reviewed API signatures. DocFX build metadata containing absolute source paths is removed before delivery, and the artifact validator rejects machine-local paths. A checksummed archive and source-file hash manifest are prepared. Public domain activation, HTTPS setup and confirmation of the domain contact/hosting details remain publication steps; the prepared artifact is not claimed to be live.
