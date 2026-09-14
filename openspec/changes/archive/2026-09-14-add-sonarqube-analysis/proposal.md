# Add SonarQube analysis

## Why

WeavePort needs quality and coverage reporting alongside CodeQL, using the existing operator-managed SonarQube service. Stratara already has both analyses enabled.

## What Changes

Add a credential-free PR coverage job for executable .NET regression suites. Analyze main source on the SonarQube server after main pushes, nightly and on manual main dispatch. Wait for the server's quality gate. Use a dedicated project analysis token.

## Impact

Analysis workflow, coverage tooling and operations documentation. No shipped dependency or runtime API changes.
