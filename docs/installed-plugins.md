# Installed plugin resolution

Choose a known plugin artifact and keep its identity stable throughout an operation. `WeavePort.Hosting` provides `InstalledPluginCatalog`, `InstalledPlugin` and `InstallationIdentity` for that selection. All three reference applications consume this API through packed NuGet artifacts. This is local integrity validation for owner-controlled deployments; it is not a downloader, signature verifier or hostile-code sandbox.

## Host integration

Construct a catalog with a trusted releases directory and a mapping of runtime aliases to approved local files. Resolve a concrete plugin ID, release and application contract ID. `Resolve` validates the manifest and returns verified entry points plus a persistable identity. On recovery, supply that same identity as `pinned`. A mismatch is refused; the resolver never chooses a fallback release.

`ReadSelection` reads a default only for a new logical operation. `Activate` validates a selected installation before atomically replacing the default selector. Existing pins do not reference that selector. The application owns when a logical operation begins and where its pin is committed.

These APIs do not launch a worker. Use the returned entry point with the approved runtime in a `ProcessProfile`, and use the resolved artifact release in `PluginContext.Version`. The existing startup guard independently rejects a worker that advertises another version. See [the API source](../src/WeavePort.Hosting/InstalledPluginCatalog.cs) and [packed consumer checks](../tests/installations/Program.cs).

## Manifest and identities

Each release directory contains `installation.json` with case-sensitive fields:

| Field | Meaning |
|---|---|
| `Schema` | Manifest format, currently 1 |
| `Plugin`, `Version` | Expected plugin installation ID and exact artifact release |
| `Contract` | Application contract identifier, independent of artifact release |
| `EntryPoints` | Trusted aliases mapped to declared relative bundle files |
| `Files` | Complete bundle file inventory with uppercase SHA-256 hashes, excluding the manifest itself |
| `RuntimeFiles` | Expected runtime aliases and file hashes; host supplies the corresponding local paths |
| `Compatibility` | Required exact host API/protocol/core packages and entry-specific SDK declarations; see [compatibility policy](package-compatibility.md) |

An `InstallationIdentity` contains plugin, version, contract and SHA-256 of the exact manifest bytes. Even a manifest-only change invalidates an old pin. A directory may be relocated with unchanged content and approved equivalent runtime files; absolute paths are not the identity. Missing/extra bundle files, links, path traversal, duplicate JSON fields, unknown schema, mismatched contract/release and changed hashes are refused. The manifest is limited to 1 MiB, 4096 files and 32 entry points; traversal is bounded to 8192 directory entries.

The [offline sealing tool](../scripts/seal-installation.py) generates manifests after building. It removes generated Python bundle bytecode caches; Decision Room launches Python with `-B` so its release directory remains unchanged. Build scripts require Python 3.11+ for sealing. C#/Python SDK files included in the bundle are hashed; Decision Room also declares its external Python SDK source modules. Runtime executable hashes are recorded through host-supplied aliases.

## Stable deployment precondition

The manifest is trusted installation metadata. Someone able to replace both metadata and files can describe a different installation for a new operation. Persisted pins still detect changed metadata, but this does not authenticate a publisher. Keep release directories, selectors and durable application state under the appropriate deployment/application authority.

Files must remain unchanged from verification through execution and any automatic worker replacement. Hashing before a call does not prevent a time-of-check/time-of-use race. No read-only mount, kernel enforcement or code snapshot is introduced. OS/.NET shared framework/Python standard library and environment dependencies outside the declared file set remain deployment-owned and must also stay fixed. Runtime executable hashing does not attest their full dependency closure.

Build/sealing are offline development actions and may replace release files. Activation of already-built installations is the supported live action. Do not rebuild, reseal or modify a release/runtime while a reference application is active. Changing shared libraries can invalidate earlier application state; retain the original build if that state must be recovered.

## Reference application behavior

| Application | Pin owner | Activation/recovery behavior |
|---|---|---|
| Decision Room | Journal configuration and artifact dictionary containing the shared manifest digest | Existing release selection remains; resume and worker replacement retain the selected release. Old development journals lacking the new identity are refused unchanged. |
| Document Workshop | One import attempt and its completed NDJSON header (`installation`) | `--version` selects an explicit release; `--activate` changes future imports. A live import retains its release, including fallback readers. Incomplete imports still have no coordinator-resume contract. |
| Appointment Desk | Installation identity in each persisted intent, calendar schema 2 | Existing requests resolve their stored installation regardless of the active default. Conflicting explicit versions fail. Schema-1 calendars are refused unchanged because their original code identity cannot be reconstructed safely. |

Document Workshop and Appointment Desk now build real release-1 and release-2 workers. Both currently implement the same v1 domain contract; the worker startup declarations differ. A new artifact release does not imply a different domain schema.

```sh
./scripts/document-workshop.sh --activate 2
./scripts/document-workshop.sh --version 1
./scripts/appointment-desk.sh --activate 2
./scripts/appointment-desk.sh --request new-v2 --store artifacts/appointment-desk/calendar-v2
```

For existing Appointment Desk development data, keep the old store and start a fresh path as shown; do not reset it in place. Decision Room similarly requires a new journal for this build unless its recorded installation identity matches. Existing Document Workshop output remains readable.

## Verification

Build the three examples with their respective `--build --verify` commands, then run `./scripts/verify-installations.sh`. The latter uses fresh package extraction, validates metadata/content guards and a real startup mismatch, and launches separate sample coordinator processes to check pinned-state preservation on refusal. All destructive fault cases operate on isolated artifact copies. See the [retained report (historical) — pre-public record](history.md) for measured scope and results.
