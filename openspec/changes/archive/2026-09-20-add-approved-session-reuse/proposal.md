# Integrate approved session reuse

## Why
The two-policy benchmark supports operator-approved cooperative reuse while unreviewed plugins retain customer-bound containers. The product currently only implements the latter.

## What Changes
- Add host-owned opt-in approved-session policy, with exact normalized deployment/version compatibility and successful SDK cleanup acknowledgement before sharing.
- Add registered invocation resources and expired-context checks to all three SDKs, preserving stream ownership until close/completion.
- Retain global resource admission, idle expiry, fail-closed retirement and scheduler fairness; expose reuse accounting.
- Add source/packed consumer tests, actual-host checks and author best practices.

## Impact
Hosting, SDKs, lifecycle/tenant-isolation/plugin-sdk/fair-scheduling contracts and documentation. No publication or version allocation. Legacy protocols remain customer-bound by default. No additional sandbox dependency.
