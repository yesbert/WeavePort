# Continuous integration and documentation delivery

All changes to `main` go through pull requests. The repository rules require successful documentation, macOS, Windows/Linux and all four CodeQL checks, an up-to-date branch and resolved review threads. Automatic Copilot review runs for ready PRs and new pushes. Rules are repository settings; workflow files alone do not enforce branch protection.

## Functional checks

CI retains the existing full macOS candidate qualification and adds `scripts/ci/native.py` on Ubuntu 24.04 and Windows Server 2025 hosted runners. The runner packs local Abstractions, Hosting and Testing packages, publishes the C# worker and runs the existing native adapter fixtures with C#, Python 3.14 and Node 24.

Windows uses stdio. Linux tests stdio and Unix sockets. Checks cover tenant-bound callbacks, workspace/state separation, environment handling, failure/cancellation, lifecycle and cleanup. Results, runtime details and the source revision are uploaded as `native-<runner>` artifacts, even when verification fails. These jobs verify source revisions; they do not qualify every Linux distribution, CPU architecture, installer, public package version or performance profile.

The [initial CI run](https://github.com/yesbert/WeavePort/actions/runs/34828099259) passed 38 Windows stdio checks, 38 Linux stdio checks and 39 Linux socket checks. All reported zero failures. The source test environments were Windows x64 and Ubuntu 24.04 x64, with Python 3.14.7 and Node 24.20.0.

To reproduce with the pinned .NET SDK, Python 3.14 and Node 24 installed:

```sh
python scripts/ci/native.py
```

Use a clean checkout: the runner creates a new evidence directory and consumes freshly packed local libraries.

## CodeQL

The CodeQL workflow analyzes C#, JavaScript/TypeScript, Python and GitHub Actions on PRs, main pushes, weekly schedules and manual dispatch. It uses GitHub's `none` build mode. This mode does not fully cover C# generated code; the CI build and functional tests remain separate checks. Findings appear in GitHub code scanning. See [CodeQL action documentation](https://github.com/github/codeql-action).

## SonarQube and coverage

The `SonarQube` workflow complements CodeQL with a .NET analysis on the operator-managed [SonarQube server](https://sonar.stratara.tech). It targets the project key `weaveport`, using the repository variable `SONAR_HOST_URL` and dedicated project-analysis secret `SONAR_TOKEN`. The hosted project uses the instance-default **Sonar way** quality gate. For another installation, provision the project and credential before running server analysis. Rotate the project-analysis token in SonarQube and replace the GitHub secret before its expiration; the initial hosted credential expires on September 14, 2027. Never commit tokens or include them in workflow arguments as literal values.

Pull requests execute the Hosting and Gateway console regression suites under pinned `dotnet-coverage` tooling, without server credentials. The job checks both the recorded test-process exit codes and the presence of covered ranges; it uploads `sonar-coverage` containing the XML report and execution logs. Coverage currently comes from these two .NET suites. It does not measure Python/TypeScript tests, plugin subprocesses that clear profiler settings, or every integration path.

After a main push, nightly at 05:00 UTC, or manual dispatch on main, a separate job builds all source-library projects inside the SonarScanner analysis and imports that run's coverage report. It waits for the server's quality gate and fails if the gate rejects the analysis. PR jobs never receive the analysis token or contact this server. Coverage is kept separate from the existing native platform tests. Uncovered source-library code remains in scope; no coverage threshold or exclusion is added merely to make a first scan green.

The [first server analysis](https://github.com/yesbert/WeavePort/actions/runs/34831996671) on September 14, 2026 imported coverage for 32 main-source files and passed the default quality gate. Its overall baseline was 45.3% coverage, 37 issues (5 reliability and 32 maintainability) and 2 security hotspots awaiting review. A passing new-code quality gate does not mean the existing code is free of findings. Current results are available in the [WeavePort dashboard](https://sonar.stratara.tech/dashboard?id=weaveport).

The scanner and collector are development tools installed at pinned versions; neither is shipped in the WeavePort NuGet packages. CodeQL continues to analyze C#, JavaScript/TypeScript, Python and Actions independently.

## Automatic website publication

A successful **main push** CI run invokes the reusable `deploy-site.yml` workflow after documentation, macOS and Windows/Linux checks pass. The deployment downloads that run's validated `documentation-site` artifact, including the generated Markdown, `llms.txt` and `llms-full.txt`. PR and scheduled runs do not deploy.

The `documentation-production` environment permits the `main` branch only. It contains `DOCS_DEPLOY_KEY` and `DOCS_KNOWN_HOSTS` secrets and the `DOCS_DEPLOY_HOST` and `DOCS_DEPLOY_USER` variables. The host key must be obtained over a trusted administrative connection. No general server-management credential is stored in GitHub.

The dedicated SSH account is restricted to the administrator-installed archive receiver from `scripts/ci/receive-site.py`. Install that file root-owned outside the account's writable directories, and configure its output root through the forced command. Both the output root and its `releases` directory must be owned and writable by the deployment account; verify this as that account before the first publication. The key cannot open an interactive shell, forward ports or choose a remote command. The receiver accepts static files only, rejects unsafe archive paths and links, limits upload size and retains previous releases. It selects a new release atomically, refuses older workflow runs, and accepts an identical retry. Changes to this server-installed receiver require a separate administrative installation.

The [first automatic main deployment](https://github.com/yesbert/WeavePort/actions/runs/34828692638) passed after correcting the initial server release-directory ownership and rerunning the failed deploy job. The earlier site remained active during that failure.

The public `deployment.json` identifies the deployed commit and CI run number. CI verifies it and compares public entry/AI files with its artifact after upload. Rollback is an administrator operation that restores the previous `current` symlink target; retained releases are not automatically deleted. Re-running a successful main CI run republishes an identical artifact, while a new main commit creates a new release.

Website publication is independent of NuGet releases. The existing `Release` workflow and its package-publication approvals remain unchanged.

### Read findings in GitHub

Open the latest main [SonarQube workflow run](https://github.com/yesbert/WeavePort/actions/workflows/sonar.yml). The analysis job summary lists every exported open issue and unreviewed security hotspot, with messages, impact or priority, rules and source locations. Download its `sonar-report` artifact for `summary.md`, complete issue data (including flows), hotspots, quality gate, coverage measures, duplication files and export metadata. Reports are retained for 30 days. This follows Stratara's Actions report workflow; it does not create GitHub issue tickets or Code Scanning alerts.

The export runs even when the quality gate fails. An incomplete export fails visibly and missing responses are never reported as zero findings. Source links target the analyzed commit only when the submitted and current server analysis identities match. If the project changes during export, links are omitted and the report is marked incomplete. Oversized job summaries point to the complete artifact.

The exporter tries the existing analysis credential. If its project-analysis scope cannot read report endpoints, store a user token with the necessary Browse access as `SONAR_REPORT_TOKEN`; keep `SONAR_TOKEN` for analysis. A token from an existing account is sufficient. User tokens inherit all permissions of that account and are not scoped to read-only access by their name or by this exporter. A separate account with only project Browse permission is an optional way to limit those permissions. Both remain confined to the trusted main analysis job. An HTTP 401/403 requires credential or permission correction, not a weaker quality gate. See [SonarQube token scopes](https://docs.sonarsource.com/sonarqube-server/user-guide/managing-tokens).

The [verified report run](https://github.com/yesbert/WeavePort/actions/runs/34835041463/attempts/2) on September 14, 2026 exported 37 issues, two unreviewed hotspots and 39 source links for revision `75fb20e86ec6f91aeb86059d22ce60080be1e932`, with no export errors. The report credential expires on September 14, 2027; rotate it in SonarQube and replace `SONAR_REPORT_TOKEN` before expiration. Use the latest successful run for subsequent remediation rather than treating these baseline counts as current.
