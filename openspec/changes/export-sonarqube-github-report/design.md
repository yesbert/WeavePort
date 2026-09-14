## Context

Stratara Analysis run 34828343542 successfully exported 44 issues despite its failing quality gate. Its scripts/ci/export-sonarqube-results.sh produces a Markdown summary and issues, hotspots, quality-gate, measures and duplications JSON artifacts. WeavePort currently has no export step.

## Decisions

Use Python standard-library HTTP/JSON support and the existing trusted main-only analysis job. No tokens reach pull requests. Export runs after submission even when the gate fails. The export step is required; an export failure is visible without masking the scanner result. Save successfully retrieved data and a clearly incomplete summary when other endpoints fail. Paginate issues, hotspots and duplication files; reject incomplete or inconsistent pagination. Escape Markdown and link paths to the analyzed revision only when current project analysis identity matches the submitted analysis. Include raw issue flows and rule identifiers for GitHub-based remediation. Keep a complete summary artifact while bounding the job summary size.

Use SONAR_REPORT_TOKEN when explicitly provisioned for Browse access, otherwise attempt existing SONAR_TOKEN. A project-analysis token may lack read privileges; never broaden permissions automatically or claim an empty report on HTTP 403. References: https://docs.sonarsource.com/sonarqube-server/user-guide/managing-tokens and https://docs.github.com/en/actions/reference/workflows-and-actions/variables.

## Verification

Test pagination, partial failure, hostile Markdown, source links, MQR impact severities, and failed quality gate reporting without live credentials. Verify the actual main Actions summary and artifact after protected PR merge. No Docker service changes are needed.
