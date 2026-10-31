## Why
Refresh maintained dependencies and provide a repeatable update workflow before shipping a verified patch release.

## What Changes
Adapt the V2 application dependency-update workflow for WeavePort: stable registry discovery, explicit reasoned pins, dry-run, lockfile refresh, build/test/audit and private synchronized skill guidance. Update runtime, test, documentation and analysis packages, adapting internal APIs where required. Allocate .NET 0.9.1 and new patch identities for changed author SDK artifacts, qualify and publish original tested artifacts.

## Capabilities
No intended public API or product behavior change. Existing installation, publisher trust, SDK and exact compatibility requirements remain applicable. Changes to upstream parser internals require existing refusal and reflection-disabled regression checks.

## Impact
Dependency manifests/locks, update tooling/tests, internal TOML integration, compatibility baselines, versioned consumer fixtures, release documentation and evidence. Historical records retain their identities. No Docker service mutation, new runtime guarantee or preview framework is included.
