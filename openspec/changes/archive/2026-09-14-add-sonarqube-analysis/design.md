# Design

Stratara already uses GitHub's CodeQL default setup and its own SonarQube workflow. Preserve both instead of creating a duplicate CodeQL workflow.

WeavePort's tests are executable programs, so dotnet test alone cannot establish coverage. Run Hosting.Tests and Gateway.Tests under dotnet-coverage, retain each actual exit code and require covered ranges in the produced XML. Analyze all src/WeavePort.* projects; uncovered source remains visible. This is .NET coverage, not Python/TypeScript coverage. Existing native and multilingual CI remains separate.

Only main jobs receive the server token; pull requests collect coverage without credentials and never contact the analysis server. The scanner is configured for .NET scope, imports Visual Studio coverage XML and waits for the quality gate. Do not weaken a failing gate or hide uncovered implementation to make the initial analysis pass. References: https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/test-coverage/dotnet-test-coverage and https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage.

## Tool license review

Exact NuGet artifacts inspected: dotnet-sonarscanner 11.3.0 (licenses/LICENSE.txt, LGPL-3.0, with bundled third-party notices) and dotnet-coverage 18.11.2 (License.txt, Microsoft .NET Library terms, plus ThirdPartyNotices.txt including Mono.Cecil MIT notices). Their original license files were retained with downloaded inspection artifacts. These are unmodified CI/developer executables downloaded from NuGet, not linked into or redistributed with WeavePort's MIT packages or website. Microsoft terms permit installation/use for application development and testing. No redistribution rights beyond the inspected terms are claimed; retain notices and reassess obligations if tooling is later bundled. They are not added to the shipped package dependency closure.

## Coverage verification

GitHub run 34831159354 executed both Hosting.Tests and Gateway.Tests with exit code zero and produced 1,908 covered ranges. The report includes actual product modules (Hosting, Abstractions, Sdk.Client and Sdk.Gateway), not only test code. The initial collector invocation rejected embedded command quoting; passing target arguments separately fixed it. The collector can return success for a failed target, so the recorded suite exit codes are checked independently. This is partial regression coverage, not a whole-product coverage claim. Server import and the quality gate remain pending project/token provisioning.

## Provisioning and server verification

The hosted project `weaveport` uses branch `main`, the instance-default previous-version new-code definition and the built-in Sonar way quality gate. A dedicated project-analysis token is stored in the GitHub repository secret; its initial expiration is September 14, 2027. The host URL is a repository variable.

PR #6 merged as `095d4cc109cd6d831dedc525b0b9f4a4eeee2618` after documentation, all native platform tests, all four CodeQL languages and executable coverage passed. Main run [34831996671](https://github.com/yesbert/WeavePort/actions/runs/34831996671) successfully submitted the analysis and received `QUALITY GATE STATUS: PASSED`. The scanner indexed 60 files and imported the coverage report for 32 main files and 11 test files. The server reported 45.3% overall coverage across 2,186 lines to cover, 37 issues (5 reliability, 32 maintainability), 2 security hotspots and 0.0% duplication. No quality threshold was reduced and no existing finding was accepted or suppressed. The initial passing new-code gate is not a clean-bill-of-health claim for the overall baseline. Remediation of baseline findings is separate product work.

## Coverage fixture stabilization

Verification PR #7 exposed a pre-existing cancellation-fixture race in Gateway.Tests (run 34832237321). The cancellation of Task.Delay could resume InvokeAsync and dispose the separately registered throwing callback before it ran, so the expected AggregateException disappeared. A single callback now both completes a TaskCompletionSource as canceled and throws the fixture exception, preserving the cleanup assertions without dependence on callback scheduling. Production code is unchanged. APIs verified against SDK 10.0.401 / net10.0 / C# 14 and Microsoft documentation for [CancellationToken.Register](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.register?view=net-10.0) and [TaskCompletionSource.TrySetCanceled](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1.trysetcanceled?view=net-10.0).

The corrected Gateway suite passed 50 consecutive local Debug runs; the GitHub coverage job rechecks it under Linux instrumentation.
