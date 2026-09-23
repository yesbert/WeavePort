# Document Workshop

An independent integration template for document readers: import a local UTF-8 file, choose an approved reader and inspect normalized sections and metadata. The application owns input capture, reader selection and committed documents. Workers own extraction. No TreeWeaver dependency, database, model service or Docker engine is required.

## Run

From the repository root, with the .NET SDK selected by `global.json`:

```sh
./scripts/document-workshop.sh --build
./scripts/document-workshop.sh --list-readers
./scripts/document-workshop.sh --file samples/DocumentWorkshop/fixtures/handbook.md --reader plain
./scripts/document-workshop.sh --file samples/DocumentWorkshop/fixtures/notes.md
```

The build packs current WeavePort NuGet libraries, restores the host and worker from those packages, and runs the default handbook import. Package restore may require internet access. Subsequent commands use the built artifacts.

The default handbook produces four sections with the Markdown reader: Handbook, Installation, Recovery and Unicode. The same file with `--reader plain` produces one logical section containing the original heading syntax. The notes fixture makes the outline reader decline, so automatic selection tries plain text. Explicit reader selection never silently substitutes another reader.

Each import prints its reader, section/fragment counts, headings and the resulting file path. Results are uniquely named NDJSON files in `artifacts/document-workshop/documents/`; a new import does not overwrite existing documents. Open that file to inspect the full normalized content.

Options: `--file PATH`, `--reader markdown|plain`, `--config PATH`, `--store PATH`, `--tenant ID`, `--profile ID`, `--list-readers` and `--verify`. The CLI has a 60-second overall deadline; Ctrl+C cancels. Tenant/profile options model trusted application context, not production authentication.

## Reader selection

`config.json` declares the installed first-party readers in preference order. The application maps only known identifiers to its built worker; configuration cannot install arbitrary executables. Both readers share a worker binary with distinct reader implementations selected by the trusted launcher. Each attempt uses a separate process/binding and verifies its description/version before extraction.

| Reader | Media types | Behavior |
|---|---|---|
| `markdown` v1 | `.md` / text/markdown | Requires an ATX heading as the first nonblank line; extracts subsequent headings outside fenced code. |
| `plain` v1 | `.txt`, `.md` | Emits one logical section and retains heading syntax as text. |

Remove a reader from the config to simulate an installation without it. An unavailable explicit reader or unsupported file type is rejected before staging. Only a declared content decline enables fallback; crashes, invalid data, quotas and cancellation fail the import.

## Source transfer and extraction

The host captures the source in its staging area, computes a SHA-256 digest and gives the plugin an opaque document ID, filename, media type and byte length. The plugin never receives a host document path.

`document.read` callbacks are bound to the import's tenant/profile and document ID. They validate byte offset/count, enforce a 32 KiB read ceiling and a cumulative transfer allowance, and stop serving bytes after lease disposal. Native workers are still trusted same-user processes; these API checks do not impose an OS filesystem sandbox.

The sample-owned `reader.open` / `reader.next` protocol consumes at most one source block per step and returns at most 32 ordered fragments. Strict incremental UTF-8 decoding preserves characters across blocks. Reader state is temporary and belongs to that worker. The host validates every page and writes it incrementally to staging; it never needs to accumulate the complete normalized document in memory.

This pull protocol keeps extraction checkpoints explicit in the example's domain contract. The SDK also supports live result streams with immediate available-item delivery, operation-wide residency and a configurable per-invocation callback budget (default eight). The example retains its explicit extraction steps for application-owned progress and recovery.

## Stored format and completion

NDJSON has three record kinds:

- `document`: filename, media type, captured length/digest, reader/version and host-bound tenant/profile.
- `fragment`: global sequence, section number, part number, heading, heading level, anchor and text.
- `complete`: final section and fragment counts.

A section with a long body spans ordered parts. Join those parts to reconstruct its body. Only a validated complete extraction that consumed the captured source can be moved into the document store. A partial file is never presented there as complete. Handled failures/cancellation revoke the source lease and remove the import's staging directory. An empty source fails without committing a document.

The outline grammar is intentionally small: ATX headings with up to three leading spaces, optional ASCII `{#anchor}` suffixes, and backtick/tilde fences. Duplicate anchors are refused; otherwise generated `section-N` anchors are stable for the same input. Links remain in body text. This is not a CommonMark implementation. Both readers strip a UTF-8 BOM and normalize line endings to LF, including the last line. Invalid UTF-8 is refused, not replaced silently.

## Explicit limits

| Scope | Limit |
|---|---:|
| Captured input | 8 MiB |
| One source read | 32 KiB |
| Reads per reader attempt | 1024 |
| Cumulative returned source bytes | source length + 32 KiB |
| One input line / one body fragment | 4096 UTF-16 characters |
| One heading / anchor | 256 / 128 characters |
| One result page | 32 fragments |
| Total fragments | 8192 |
| Normalized output | 32 MiB |

Oversize input, lines, output or malformed pages fail without a truncated commit. These are application limits, not OS memory ceilings or aggregate multi-user quotas. The worker buffers one decoded input block and its bounded pending fragments; it does not materialize the complete document.

## Verify and adapt

```sh
./scripts/document-workshop.sh --build --verify
```

The checks cover reader substitution/fallback, supported headings/anchors/fences, large Unicode input, source digest, line endings, invalid encoding, source/output limits, missing grants, invalid pages, cancellation after output, actual worker termination and overlapping tenant/profile imports. Direct lease tests additionally check foreign identities/ranges, transfer budgets and post-disposal revocation. Fault hooks exist only in the host test harness, not reader operations.

`Contracts` is sample-owned. `Worker` implements the two readers through `WeavePort.Sdk`; `Host` consumes Hosting/Sdk.Client packages. Only the sample contract module is a project reference. The three original products and Decision Room remain independent.

Verified scope is native local macOS on .NET 10. No PDF/OCR, search quality, Windows/Linux, remote execution, Docker or hostile-plugin qualification is claimed. Cleanup covers handled errors and cancellation; coordinator-crash orphan recovery and power-loss durability remain future product work. Do not rebuild trusted worker artifacts while imports are running.

The [retained verification report (historical) — pre-public record](../../docs/history.md) records the 41 passing assertions and tested scope.

## Shared installation identity

This sample now uses the packaged [installed-plugin resolver](../../docs/installed-plugins.md). Builds seal release directories; resolution validates content and contract identity. Activation changes future operations only. Do not modify or rebuild executing release/runtime files. Manifest sealing uses the .NET Hosting API and requires no Python. The C# providers require only the documented .NET build/runtime prerequisites.

Both release 1 and release 2 implement the same domain contract. Use `--activate 2` to change the default or `--version 1` for explicit selection. The selected installation is recorded with the result or persistent intent.
