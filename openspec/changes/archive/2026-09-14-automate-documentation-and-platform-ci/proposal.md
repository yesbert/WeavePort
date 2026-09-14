# Automate documentation and platform CI

## Why

The public documentation needs automatic delivery after reviewed changes, and Windows/Linux need recurring functional evidence alongside macOS.

## What Changes

Link the documentation in GitHub metadata. Add native Windows/Linux packaged adapter tests, CodeQL analysis, and a restricted static-site deployment from successful main CI. No new runtime guarantee or package release.

## Impact

GitHub workflows, deployment tooling, CI documentation and platform evidence. Existing native fixtures remain the source of functional assertions.
