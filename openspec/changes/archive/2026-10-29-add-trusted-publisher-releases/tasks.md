## 1. Public contracts and format

- [x] 1.1 Recheck current .NET crypto APIs/providers and define reviewed signing, offline verification, immutable trust-policy and authenticated-result contracts without changing existing constructors.
- [x] 1.2 Define the bounded version-1 detached signature envelope, exact signed bytes, key fingerprint, RSA-PSS parameters and independent interoperability fixtures.
- [x] 1.3 Define stable publisher refusal codes and discovery metadata, preserving standard exception categories and execution-uncertainty semantics.

## 2. Offline signing and trust

- [x] 2.1 Implement generated-metadata envelope parsing, strict duplicate/unknown-field and size rejection, key import validation and copied immutable trust snapshots.
- [x] 2.2 Implement offline bundle validation and atomic signature writing using caller-owned private-key instances; verify no runtime/plugin execution or manifest/pin mutation.
- [x] 2.3 Reserve only the root signature sidecar in inventory, reject manifest references to it, preserve nested ordinary files and refuse resealing while a sidecar exists.
- [x] 2.4 Implement required-signature verification with no release-provided trust, network dependency or unsigned fallback.

## 3. Catalog integration and regression evidence

- [x] 3.1 Apply policy consistently to staging validation, sync/async resolution, discovery, activation and pinned recovery before runtime probes; assert unchanged selectors on failure.
- [x] 3.2 Verify valid releases from two publishers, unknown keys, unsigned/schema-1 refusals, malformed/oversized/linked envelopes, unsupported algorithms and tampered manifest/bundle/inventory.
- [x] 3.3 Verify key overlap, re-signing with stable pins, withdrawal under replacement snapshots, concurrent catalogs and the documented existing-handle/worker revocation boundary.
- [x] 3.4 Verify unsigned legacy compatibility, detached sidecar migration, reflection-disabled installation operations, independent crypto vectors and freshly packed public consumer APIs.

## 4. Documentation and qualification

- [x] 4.1 Add a publisher pipeline and consumer upload-staging example with separate key/trust/installation authority, protected immutable promotion, key rotation and withdrawal procedures.
- [x] 4.2 Document catalog-wide publisher authority, unsupported rollback prevention, running-worker limitations, old-reader incompatibility and signature-versus-sandbox boundaries.
- [x] 4.3 Update public API baseline, consumer/security guides and generated documentation; run focused tests, public-tree, architecture, style and strict OpenSpec checks.
- [x] 4.4 Qualify committed candidate with the normal verification workflow, record exact platform evidence, then synchronize verified deltas and archive the change.
