# Historical evidence and the public baseline

The public repository starts from a reviewed source baseline. Earlier development commits, completed OpenSpec changes and exploratory reports are retained separately by the maintainers and are not part of this Git history.

Current product specifications are in `openspec/specs/`; unfinished work is in `openspec/changes/`. Changes completed after the public baseline are archived normally under `openspec/changes/archive/` and remain versioned with the product. The exclusion of older archives is a one-time baseline decision.

Some guides describe historical experiments or refer to exact pre-public delivery identities. Those records explain the origin and limits of the current design; they are not independently retrievable through this public repository's Git history. A historical source hash is provenance, not a commit that a public clone can resolve. Do not treat a pre-public measurement as qualification of a later source revision.

For current behavior, use the [architecture](architecture.md), [product status](status.md), [package compatibility](package-compatibility.md) and [benchmarking guidance](benchmarking.md). Retained release reports in this tree identify the exact original artifacts they measured. New qualification runs record the current public commit and newly built artifacts.
