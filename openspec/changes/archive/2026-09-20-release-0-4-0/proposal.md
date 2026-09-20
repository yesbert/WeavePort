# Audit and release WeavePort 0.4.0

## Why
The approved-session reuse and fair scheduling candidate needs a consistent release identity and clean-checkout qualification. The candidate verifier currently omits the new multilingual reuse tests, and the non-.NET SDK labels still identify their pre-cleanup versions.

## What Changes
- Audit lifecycle ownership, scheduling, SDK cleanup, package/API consistency and maintained documentation.
- Include source and packed reuse tests, and packed author examples, in the frozen candidate gate.
- Version the four core packages at 0.4.0 and Python/TypeScript SDKs at 0.2.0; keep the documented separate registry-publication boundary.
- Qualify committed source, merge with green CI, publish through the existing NuGet Trusted Publishing workflow and retain exact release evidence.

## Impact
Release metadata, compatibility inputs, candidate/release tooling, current documentation and consumer lockfiles. Historical evidence retains its original version labels. No additional isolation boundary or maximum-capacity guarantee is introduced.
