# Publisher trust for installed releases

**Availability:** Included in the published 0.9.0 package family; see [release evidence](../reports/release/0.9.0/README.md).

Installation hashes protect content against a manifest. Publisher signatures authenticate that manifest against a host-owned public key. Require publisher authentication when accepting releases from callers whose installation credentials must not grant authority to introduce arbitrary code.

## Pipeline and host APIs

Seal built files with `PluginInstallationSealer.Seal`, then call `PluginInstallationSigner.Sign(releaseDirectory, plugin, version, contract, privateKey)`. The signer validates the complete sealed schema-2 bundle before atomically replacing only `installation.signature.json`. It neither executes code nor probes runtimes, changes selectors or rewrites manifest bytes/pins. It uses the supplied RSA instance without taking ownership, discovering keys or persisting private material. Keep signing keys in the publisher pipeline, outside all release files and the host upload API.

Configure trust separately from installation credentials:

```csharp
var publisher = new TrustedPluginPublisher("release-pipeline", publicKeyDer);
var policy = PluginPublisherPolicy.RequireTrustedPublisher([publisher]);
var catalog = new InstalledPluginCatalog(pluginRoot, approvedRuntimeFiles, policy);
var installation = await catalog.ResolveAsync(plugin, version, contract, pinnedIdentity);
VerifiedPluginPublisher? authenticated = installation.Publisher;
```

`TrustedPluginPublisher` validates and copies one canonical DER SubjectPublicKeyInfo RSA public key. `PluginPublisherPolicy` copies the publisher set; it has no mutable trust collection. Multiple publishers and overlapping rotation keys are supported. Duplicate fingerprints, unsupported key sizes and an empty required trust set fail configuration. Publisher labels come from host configuration, never from release metadata. All keys in a catalog authorize every plugin it admits; separate catalogs and application authorization are needed for partner-specific scope.

`PluginInstallationVerifier.Verify(releaseDirectory, plugin, version, contract, policy, pinned)` is the offline staging API. It authenticates the manifest and checks complete bundle content and declarations without probing runtimes. It returns `PluginInstallationVerification` with the identity and authenticated publisher. External files and installed runtime compatibility are deployment-owned checks performed by catalog resolution. Keep staging exclusively owned and immutable through same-filesystem promotion to protected release storage. See the [compiled pipeline and staging examples](../examples/signed-releases/README.md).

The policy is enforced by sync/async resolution, listing, diagnostic discovery, activation and pinned recovery before runtime probes and plugin dispatch. Failed activation leaves the selector unchanged. `ListAll`/`ListAllAsync` return publisher refusal codes alongside valid siblings. Normal identity, content, compatibility and runtime checks remain mandatory after signature verification.

## Rotation, withdrawal and existing workers

Add the new public key before switching the pipeline. Re-signing unchanged manifest bytes with a new trusted key preserves installation identity. Remove the old key by replacing the installer/catalog trust snapshot and stopping new admissions through the old snapshot. Re-resolution under the replacement policy rejects a withdrawn key even when an old persisted pin still matches.

A verified publisher result is an observation at verification time, not a live revocation capability. Previously returned installations, existing bindings, resident workers and their automatic replacements are not retroactively revoked. For immediate revocation, the host must stop admissions, dispose affected bindings/workers and re-resolve using its replacement policy. Trust replacement does not itself terminate processes.

## Detached signature format

Schema-2 manifests retain their exact bytes and SHA-256 identity. The reserved root file `installation.signature.json` is detached metadata; it cannot be declared as hashed bundle content or as an entry point. A nested file with this name is ordinary hashed content. Other inventory, link and path checks remain intact. Sealing a release with an existing root signature refuses without changing either file: remove it explicitly in offline staging before changing/resealing content.

The envelope contains exactly `Schema` (1), `Algorithm` (`RSA-PSS-SHA256`), `KeyId` and `Signature`. Unknown/duplicate/missing fields, null or invalid values, noncanonical base64, links and envelopes over 16 KiB are refused. Unknown versions/algorithms are refused without negotiation. `KeyId` is lowercase SHA-256 hex of canonical DER SubjectPublicKeyInfo. `Signature` is canonical base64 of the RSA signature.

The signed message is UTF-8 `WeavePort.InstallationSignature.v1\n` followed by the exact `installation.json` bytes, including whitespace. RSA-PSS uses SHA-256, MGF1-SHA-256 and 32 salt bytes with 3072- or 4096-bit keys. Reformatting a signed manifest invalidates its signature. Signing may produce different signature bytes for identical input without changing the manifest pin. No certificate trust, embedded public key, remote key lookup or online revocation is used.

## Refusals and compatibility

`PluginSignatureException` retains the `IOException` category and exposes `ErrorCode`. Diagnostic discovery exposes the same code in `Refusal`. Messages explain the cause; do not parse them or infer retry safety from a code.

| Code | Meaning |
| --- | --- |
| `publisher-signature-missing` | Required root signature is absent. |
| `publisher-signature-malformed` | Envelope is malformed, ambiguous, linked or oversized. |
| `publisher-signature-unsupported` | Envelope schema or algorithm is unsupported. |
| `publisher-key-untrusted` | Fingerprint is absent from this snapshot, including withdrawn keys. |
| `publisher-signature-invalid` | Signature does not authenticate the manifest bytes. |
| `publisher-schema-unsupported` | Publisher authentication/signing requires offline migration to schema 2. |

Configuration errors retain argument/cryptographic exception categories; ordinary filesystem failures retain their I/O/access categories. No keys, signatures or raw manifests are added to default logs.

The existing catalog constructor and `PluginPublisherPolicy.IntegrityOnly` preserve unsigned schema-1/schema-2 operation and make no authenticity claim (`Publisher` is null). New readers recognize detached metadata. Older readers reject signed bundles as an extra inventory file: upgrade readers before distributing signed releases. Required policy never falls back to unsigned acceptance. A legacy bundle using the reserved root filename as content must rename it and be resealed. Schema-1 migration changes identity and never rewrites persisted pins automatically.

Signatures are not a sandbox, a claim of safe code, runtime integrity or freshness. A trusted publisher can sign vulnerable code; a stolen installation credential can still select older signed releases unless the application enforces version-admission policy. Protect public-key configuration with authority separate from installation credentials. Hostile same-user modification of deployment files remains outside the stable-files guarantee.

## Verification scope

`tests/WeavePort.Publisher.Tests` verifies installation operations with JSON reflection disabled, independent OpenSSL fixture compatibility, byte-preserving signing, malformed/tampered releases, multiple publishers, copied trust snapshots, rotation/withdrawal, mixed discovery, unchanged selectors and an actual worker surviving trust snapshot replacement. The worker/lifecycle subprocess uses the existing reflection-enabled author/runtime path; this does not qualify all Hosting functionality for Native AOT. The portable script repeats the consumer suite against fresh packages. No broader platform or containment claim follows from cryptographic API availability.
