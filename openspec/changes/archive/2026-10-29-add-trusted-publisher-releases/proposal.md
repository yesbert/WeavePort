## Why

Installation hashes detect changes against trusted metadata but do not authenticate who produced a release. Hosts accepting runtime uploads need an independent publisher trust boundary so possession of an installation credential alone cannot authorize arbitrary executable code.

## What Changes

- Add offline signing of sealed schema-2 releases and verification against a host-owned set of trusted publisher public keys.
- Add an explicit required-signature policy for installation validation, discovery, activation and pinned resolution; preserve existing integrity-only callers.
- Support multiple publishers, overlapping keys during rotation, key withdrawal and actionable structured refusals.
- Document separate authority for installation credentials, publisher private keys and host trust configuration.
- Non-goals: a downloader, public store, certificate authority, online revocation service, automatic updates, rollback prevention, emergency termination of existing workers or hostile-code containment.

## Capabilities

### New Capabilities
- `plugin-publisher-trust`: Offline publisher authentication, host trust policy, key rotation and withdrawal.

### Modified Capabilities
- `installed-plugin-resolution`: Apply required publisher authentication to catalog entry points while retaining existing integrity and pin guarantees.

## Impact

WeavePort.Hosting installation APIs and manifest inventory handling; packed .NET consumer tests; installed-plugin, security and release-pipeline documentation. The proposal uses framework cryptography without a new runtime dependency. It does not change native worker protocol or tenant isolation. Consumers own upload staging and atomically promoting verified immutable releases. This is proposed behavior, not a current release guarantee.
