## Context

See proposal.md for the threat and scope. Current installation identity hashes the exact installation.json bytes; inventory excludes only that manifest. Existing catalogs trust local metadata and have no publisher policy. Runtime probes use host-approved executables. Tenant isolation and scheduling are unchanged by this proposal.

## Goals / Non-Goals

Authenticate sealed release metadata before a consumer admits uploaded code. Keep publisher key custody independent of host installation credentials and keep trust configuration outside uploaded releases. Preserve exact manifest pins across signing and key rotation. This is not full Native AOT, platform, sandbox or software-supply-chain qualification.

## Decisions

### Detached signature over exact bytes

Add one reserved root sidecar, installation.signature.json, for schema-2 releases. Its version-1 envelope has exactly Schema, Algorithm, KeyId and Signature fields; duplicate/unknown fields, unsupported versions and algorithms, invalid base64, links and oversized envelopes are refused. Cap the envelope at 16 KiB. No embedded public key or URL can establish trust.

Sign UTF-8 bytes of `WeavePort.InstallationSignature.v1\n` followed by the exact manifest file bytes. Use RSA-PSS with SHA-256, MGF1-SHA-256 and a 32-byte salt, with RSA keys of 3072 or 4096 bits. Algorithm is the fixed identifier `RSA-PSS-SHA256`. KeyId is the lowercase SHA-256 hex fingerprint of canonical DER SubjectPublicKeyInfo; it selects a host-configured key, not an assertion of publisher authority. The trust entry maps that fingerprint to a host-owned publisher label. Verification recomputes the fingerprint when loading configuration and rejects duplicates or invalid keys.

Use .NET framework RSA APIs, not custom cryptography or RSACryptoServiceProvider. Verify interoperability with an independent fixture before release. Framework sources consulted on 2026-10-29: [RSA.VerifyData](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rsa.verifydata?view=net-10.0), [RSA.ImportSubjectPublicKeyInfo](https://learn.microsoft.com/dotnet/api/system.security.cryptography.rsa.importsubjectpublickeyinfo?view=net-10.0). Recheck APIs and supported OS providers during implementation.

Canonicalizing JSON was rejected: exact bytes already define persisted pins and avoid another serialization contract. An embedded signature was rejected because it would introduce self-reference and change existing identity semantics. Certificates, transparency logs and online revocation introduce authorities and availability dependencies outside this request. RSA-PSS uses existing .NET providers; alternative algorithms can be a later versioned change rather than algorithm negotiation now.

### Inventory and sealing

The sidecar is detached metadata, never executable bundle content. Reserve only its exact root path; nested files with that name remain ordinary hashed files. Reject a manifest that lists the reserved root sidecar as executable content or an entry point. Do not weaken link, path, size or full-inventory checks for other files. A legacy bundle already using that root filename must rename it and be resealed before migration.

Signing validates the existing schema-2 manifest and complete bundle without executing plugins or runtime probes, then atomically writes the sidecar. It does not reseal, change selectors or rewrite pins. The caller supplies an RSA signing instance and owns its lifetime; no private-key discovery, persistence or logging occurs in WeavePort. A release pipeline can load its protected key before calling the API. Generic remote KMS integrations are outside this first implementation.

Sealing refuses a release with an existing signature sidecar, with an actionable instruction to remove it explicitly in offline staging before resealing. This avoids claiming an atomic update of two files or leaving a stale signature after a successful seal. Re-signing unchanged bytes replaces only the sidecar; randomized signature bytes do not change installation identity.

### Host-owned policy and integration

Keep the existing catalog constructor and integrity-only behavior for existing unsigned installations. Add an overload/options contract with an immutable copied trust policy: IntegrityOnly or RequireTrustedPublisher. RequireTrustedPublisher with no keys fails configuration validation. New readers recognize the reserved sidecar in both modes; IntegrityOnly makes no authenticity claim. Signed bundles require upgraded readers: older readers reject the new extra file under their current inventory rule.

Expose offline release verification suitable for consumer-owned upload staging, and route synchronous/asynchronous resolution, listing, diagnostic discovery, activation and pinned recovery through the same policy. Authenticate the bounded manifest bytes before runtime probes and before returning a usable installation, then retain complete content, compatibility and runtime checks. Missing signatures, unknown/withdrawn keys, malformed signatures and invalid signatures produce distinct documented stable refusal codes; discovery preserves these codes without leaking keys or raw data. Do not infer retry safety from them.

Trust is an immutable snapshot per catalog. Rotation adds the new key before switching the pipeline; withdrawal replaces the host's catalog/policy snapshot and stops new admissions through the old one. New validation and pinned resolution under the replacement snapshot reject the removed key even if the manifest pin matches. An already returned installation, existing binding, resident worker or its automatic replacement is not retroactively invalidated by this API. Hosts requiring immediate revocation must stop admissions, dispose affected bindings/workers and re-resolve under the new policy. This lifecycle boundary must be visible in API documentation and the integration example.

Trust grants authority to sign any plugin admitted by that catalog, not an exclusive namespace or plugin identity. Hosts needing partner-specific scopes use separate catalogs/trust sets and application authorization. This is explicit to avoid implying that a partner cannot sign an identically named plugin. Publisher naming comes from host configuration, never unverified release labels.

### Ownership and verification

Publisher policy belongs to host deployment, independently of tenant bindings; requests cannot select or replace it. Keep no mutable per-tenant signature cache. Existing worker isolation units remain unchanged. Test two tenants selecting the same artifact: rejection under one catalog's trust set must not change the other catalog or a different trusted publisher's admission. No new latency or containment guarantee is inferred.

## Risks / Trade-offs

- Compromised publisher key → remove it from the host trust set, reconcile existing bindings explicitly, and re-sign reviewed releases with a new key. No offline design detects compromise before withdrawal.
- Stolen operator key → cannot install newly forged releases under required policy; it can still select older valid signed releases unless application policy prevents rollback.
- Mutable deployment files after verification → consumer stages under an exclusive directory and atomically promotes to protected immutable storage; existing stable-files prerequisite remains. This change does not introduce an OS filesystem sandbox.
- Host trust configuration writable through installation credentials → the intended boundary collapses; document and test separate configuration authority in the example.
- Signing authenticates executable bytes and declarations → it does not prove safe plugin behavior, secure dependencies or publisher review quality.
- Older readers reject detached metadata → upgrade readers before publishing signed releases. Never silently remove required trust on rollback.

## Migration Plan

1. Upgrade host readers while retaining existing unsigned operation explicitly.
2. Provision publisher public keys through the deployment configuration authority, separately from API installation credentials.
3. Seal and sign immutable releases in the publisher pipeline; stage and verify them through the public offline API before promotion.
4. Enable RequireTrustedPublisher only after selected releases are signed; unchanged manifest pins remain valid.
5. Rotate with overlapping trusted keys, then replace policy snapshots and revalidate. For rollback, deploy a reader supporting the same trust policy or stop admission; do not downgrade to unsigned acceptance automatically.

## Implementation notes

The implemented public surface uses `TrustedPluginPublisher`, `PluginPublisherPolicy.IntegrityOnly` / `RequireTrustedPublisher`, `PluginInstallationSigner.Sign` and `PluginInstallationVerifier.Verify`. `InstalledPlugin.Publisher` and `PluginInstallationVerification.Publisher` expose immutable observed authentication. `PluginSignatureException.ErrorCode` is preserved in diagnostic discovery. Offline validation deliberately leaves runtime compatibility and external-file contents to catalog resolution; it validates their bundled source declarations without executing anything.

The existing .NET providers use PSS salt length equal to hash size, verified against an independent OpenSSL fixture. Additional official reference: https://learn.microsoft.com/dotnet/api/system.security.cryptography.rsasignaturepadding.pss?view=net-10.0 . SDK 10.0.403, net10.0 and C# 14 were checked before implementation. Exact qualification results belong in verification.md; no new package version or platform support follows from this implementation.
