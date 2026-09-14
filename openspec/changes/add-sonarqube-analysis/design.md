# Design

Stratara already uses GitHub's CodeQL default setup and its own SonarQube workflow. Preserve both instead of creating a duplicate CodeQL workflow.

WeavePort's tests are executable programs, so dotnet test alone cannot establish coverage. Run Hosting.Tests and Gateway.Tests under dotnet-coverage, retain each actual exit code and require covered ranges in the produced XML. Analyze all src/WeavePort.* projects; uncovered source remains visible. This is .NET coverage, not Python/TypeScript coverage. Existing native and multilingual CI remains separate.

Only main jobs receive the server token; pull requests collect coverage without credentials and never contact the analysis server. The scanner is configured for .NET scope, imports Visual Studio coverage XML and waits for the quality gate. Do not weaken a failing gate or hide uncovered implementation to make the initial analysis pass. References: https://docs.sonarsource.com/sonarqube-server/analyzing-source-code/test-coverage/dotnet-test-coverage and https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage.

## Tool license review

Exact NuGet artifacts inspected: dotnet-sonarscanner 11.3.0 (licenses/LICENSE.txt, LGPL-3.0, with bundled third-party notices) and dotnet-coverage 18.11.2 (License.txt, Microsoft .NET Library terms, plus ThirdPartyNotices.txt including Mono.Cecil MIT notices). Their original license files were retained with downloaded inspection artifacts. These are unmodified CI/developer executables downloaded from NuGet, not linked into or redistributed with WeavePort's MIT packages or website. Microsoft terms permit installation/use for application development and testing. No redistribution rights beyond the inspected terms are claimed; retain notices and reassess obligations if tooling is later bundled. They are not added to the shipped package dependency closure.

## Coverage verification

GitHub run 34831159354 executed both Hosting.Tests and Gateway.Tests with exit code zero and produced 1,908 covered ranges. The report includes actual product modules (Hosting, Abstractions, Sdk.Client and Sdk.Gateway), not only test code. The initial collector invocation rejected embedded command quoting; passing target arguments separately fixed it. The collector can return success for a failed target, so the recorded suite exit codes are checked independently. This is partial regression coverage, not a whole-product coverage claim. Server import and the quality gate remain pending project/token provisioning.
