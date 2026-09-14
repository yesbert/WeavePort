## Why

SonarQube findings are currently only available on the analysis server. Stratara exports its findings and metrics into the GitHub Actions job summary and an analysis-report artifact; WeavePort needs the same review entry point.

## What Changes

Export open issues, unreviewed hotspots, quality gate, coverage and duplication data after server analysis, including failed quality gates. Publish a readable GitHub summary and complete JSON artifacts with source locations. Explicitly report incomplete exports rather than representing failed requests as zero findings.

## Capabilities

No product behavior changes (`skip_specs: true`).

## Impact

SonarQube workflow, Python standard-library export tooling, focused exporter tests and CI documentation. No GitHub issue tickets or Code Scanning alerts are created: Stratara uses workflow summaries/artifacts. No runtime changes or quality-gate weakening.
