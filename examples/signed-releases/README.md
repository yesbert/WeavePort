# Sign and admit plugin releases

These two C# source files are copyable integration examples and compile/run as linked sources in `tests/WeavePort.Publisher.Tests`. They use the current Hosting source APIs; no released package version is claimed yet.

`ReleasePipeline.SealAndSign` takes the built release options and a caller-owned RSA private-key instance obtained from the publisher pipeline's protected key storage. It seals, then signs without executing the plugin or a runtime. Keep that key out of release directories, command-line arguments, logs and the installing host. Dispose it after the pipeline operation. Re-sign unchanged content with `PluginInstallationSigner.Sign`; to change content, explicitly remove its old signature in offline staging before resealing.

`StagedInstallation` receives only public-key trust configured at deployment startup. For example:

```csharp
var own = new TrustedPluginPublisher("own-pipeline", File.ReadAllBytes(ownPublicKeyDer));
var partner = new TrustedPluginPublisher("partner", File.ReadAllBytes(partnerPublicKeyDer));
var policy = PluginPublisherPolicy.RequireTrustedPublisher([own, partner]);
var installer = new StagedInstallation(protectedPluginRoot, policy);
var verified = installer.Install(exclusiveStagingDirectory, plugin, version, contract);
var catalog = new InstalledPluginCatalog(protectedPluginRoot, approvedRuntimes, policy);
var installation = await catalog.ResolveAsync(plugin, version, contract, verified.Identity);
```

Authenticate and authorize the upload caller separately. The upload API cannot accept a replacement policy or public keys. The host must safely extract bounded uploads (reject path traversal and links), own staging exclusively, prevent concurrent writers, protect the destination and keep staging/destination on the same filesystem. `Directory.Move` promotes only to a new release path and does not replace an existing version. Files must remain immutable after verification. Offline verification does not validate external-file contents or installed runtime compatibility; catalog resolution performs those deployment checks before activation. No selector is changed by this example.

For rotation, provision the new public key alongside the old one before switching the release pipeline. Re-signing unchanged bytes preserves the existing manifest pin. For withdrawal, stop admissions through the old installer/catalog and replace both with snapshots excluding the withdrawn key. Existing installations, bindings and workers are not automatically revoked. For immediate withdrawal, stop affected admissions, dispose affected bindings/workers, and re-resolve under the replacement catalog before resuming. Do not silently switch to integrity-only acceptance.

Every configured publisher can sign any plugin admitted by this catalog. Use separate protected roots/catalogs and application authorization when partner authority must be scoped. Signatures do not reject older valid releases or sandbox publisher code. See [publisher trust](../../docs/publisher-trust.md) for the format, failure codes and limits.

Run the executable checks (source, reflection-disabled installation operations plus an actual worker subprocess):

```sh
dotnet run --project tests/WeavePort.Publisher.Tests -c Release
```

`./scripts/verify-portable.sh` repeats these checks against freshly packed packages.
