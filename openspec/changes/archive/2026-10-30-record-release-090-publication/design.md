## Decisions
Retain separate preparation and exact-tag records, since prepublication quality corrections changed the final source revision. Publish only artifacts exported from the successful exact-tag qualification. Compare GitHub assets by SHA-256 and NuGet ZIP payloads excluding the added repository signature. Preserve the tag snapshot; update publication status on main through a documentation PR.

## Limits
Author SDK versions remain 0.4.0. No separate registry, new platform, containment or performance claim is introduced. The final analysis reports zero open issues and unreviewed hotspots; this is automated analysis and agent review, not an independent human review.
