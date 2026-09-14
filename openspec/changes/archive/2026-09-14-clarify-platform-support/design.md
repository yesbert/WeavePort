# Design

Support describes the implemented trusted stdio path on Windows, Linux and macOS. Release validation remains macOS arm64; Windows and Linux current-release validation is pending. Do not promote historical Linux VM experiments to bare-metal or current-package evidence. Keep Unix-socket and Bash/offline-bundle restrictions beside relevant instructions. Rejected: blanket tested/production-ready claims, and retaining Mac-specific product positioning.

Publish using a dedicated Plesk virtual host and HTTPS, preserving the Stratara document root. Retain versioned artifacts and checksums for rollback. No runtime tests are claimed by this documentation-only change.

## Verification and delivery

Website build: 39 HTML files, 465 local references, no warnings/errors. Six website tests, maintained-link checks, public-tree validation, generated LLM freshness, strict OpenSpec validation and diff checks passed. Public HTTPS responses matched the built bytes for the entry page, quickstart, platform and package guides, logo, CSS/JS, search index, sitemap, both LLM files and legal pages. HTTP redirects to HTTPS; the Stratara entry page remained byte-identical. Certificate hostname validation passed. Website is published at https://weaveport.dev/.
