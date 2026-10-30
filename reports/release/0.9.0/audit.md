# 0.9.0 preparation review

## Scope and package identities

PR #47 was merged into main as `cffc7e69732f447de307796088d25a7db7a85769`. This preparation allocates .NET 0.9.0 for its additive trusted publisher APIs and reflection-free installation JSON fix. It does not modify runtime behavior or public API snapshots.

The seven-package release allowlist remains unchanged; Testing stays internal. All selected .NET package references, loaded-version checks and embedded compatibility declarations advance together. Consumer lockfiles were restored from locally packed 0.9.0 packages. Review confirmed their only semantic changes are WeavePort versions; external dependency identities and topology are unchanged. NuGet also updates one lockfile format from version 1 to 2.

Author SDK sources and shared protocol definitions are unchanged from tag v0.8.0. Python/TypeScript remain 0.4.0, host API remains 2 and native protocols remain 1/2. No dependency upgrade is included.

## Independent fixtures and migration

Both portable and signed manifest fixtures use the exact 0.9.0 matrix. Their SHA-256 identities were recomputed independently. The signed fixture was regenerated with OpenSSL RSA-3072/PSS, SHA-256/MGF1-SHA-256 and a 32-byte salt; the temporary private key was discarded. Only test public-key, envelope and manifest bytes are tracked. Reflection-disabled sealer and publisher tests pass with these fixtures, including real worker execution and policy replacement behavior.

Migration guidance requires offline rebuild/reseal, a new signature for changed manifest bytes and deliberate application-pin migration. Existing deployments remain available for existing recovery pins. Strict trust is opt-in and catalog-wide; replacing a snapshot does not revoke handles or running workers. Signatures do not sandbox code, scope publisher namespaces or prevent rollback.

## Documentation and review limits

Current examples and generated retrieval content target prepared 0.9.0. Availability text distinguishes this preparation from published 0.8.0; historical release evidence and migration sections retain their identities. Website tests, link checks, public-tree checks, strict OpenSpec validation and whitespace checks pass.

This is an agent review of release preparation, not an independent human or Copilot approval. Clean-candidate evidence is retained separately. Native macOS arm64 qualification does not establish new Windows/Linux public-package, Docker, capacity or performance guarantees. No services were stopped or reconfigured. Tagging, NuGet publication and public-download verification remain outside this preparation.
