## Purpose

Let a host hand a plugin function named files to read at any offset, and receive named files from it into host-supplied destinations. All bytes pass only through consumer-supplied readers and writers, under host-owned limits.

## ADDED Requirements

### Requirement: Host-named positional inputs
A host SHALL be able to invoke an SDK function with named inputs, each with a validated name, a fixed length, a media type and a host-supplied positional reader. Descriptors SHALL travel as host authority outside the application payload. A plugin SHALL open only inputs named for its invocation and read any byte range within the declared length in any order: a seekable stream in C#, a seekable binary file in Python, `read(position, length)` in TypeScript.

#### Scenario: Reading the end of an archive first
- **WHEN** a plugin opens a 9 MB ZIP input and reads its central directory at the end before reading its entries
- **THEN** each read returns the exact bytes at the requested offsets, and the host reader never receives a request beyond the declared length

#### Scenario: Unnamed input
- **WHEN** a plugin requests an input name that the host did not name for the invocation
- **THEN** the request fails with `input-not-found` and the host reader is not called

#### Scenario: Late read
- **WHEN** a plugin reads an input after its invocation has ended
- **THEN** the read is refused like a late callback, and the host reader is not called

### Requirement: Transactional host-destined outputs
A plugin SHALL be able to create named outputs with a media type and write each sequentially into a host-supplied writer. The host SHALL commit outputs only after a successful call in which every created output was completed, reporting name, media type and length. On any other outcome it SHALL abort every created output through its writer with a reason; abort failures are cleanup failures and do not replace the primary outcome. `MayHaveExecuted` keeps its meaning.

#### Scenario: Successful call with an output
- **WHEN** a function writes and completes a 9 MB output and then returns a JSON result
- **THEN** the caller receives the result together with the output's name, media type and exact length, and the destination writer has been committed once

#### Scenario: Failure after an output was completed
- **WHEN** a function completes an output and then throws, is cancelled, times out or loses its worker
- **THEN** the writer is aborted with the matching reason and never committed, and the caller observes the failure

#### Scenario: Output left open
- **WHEN** a function returns successfully while an output it created is not completed
- **THEN** the call fails with `output-incomplete` and every created output is aborted

#### Scenario: Partial commit failure
- **WHEN** committing the second of two outputs fails
- **THEN** the call fails with `output-commit-failed` and `MayHaveExecuted = true`, the uncommitted writer is aborted, and the already committed output is reported as committed

### Requirement: Host-owned file limits
For each invocation the host SHALL set the maximum number of outputs and their total size. Exceeding either limit SHALL fail the call with `output-limit` before the excess reaches a writer. Blocks SHALL carry 4–256 KiB of decoded bytes. File operations SHALL NOT consume the application callback budget, and SHALL remain bounded by the invocation deadline.

#### Scenario: Output size exceeded
- **WHEN** a plugin writes beyond the invocation's total output size
- **THEN** the write is refused, the call fails with `output-limit`, and every created output is aborted

#### Scenario: Outputs not permitted
- **WHEN** a call permits no outputs and the plugin creates one
- **THEN** creation fails with `output-not-permitted` and no destination writer is created

### Requirement: Input read budget
The host SHALL count the input bytes its readers deliver per invocation, across all inputs, against a budget. The budget defaults to four times the sum of the declared input lengths, and a host may set it explicitly. A read that would exceed the budget SHALL be refused before the reader is called, failing the call with `input-read-limit`. Reads served from the SDK's block cache SHALL NOT count.

#### Scenario: Plugin reading in a loop
- **WHEN** a plugin keeps re-reading a 9 MB input under the default budget
- **THEN** the call fails with `input-read-limit` once the host readers would deliver more than 36 MB, without waiting for the deadline

#### Scenario: Cached re-reads
- **WHEN** a plugin repeatedly reads a range that is still in the SDK's block cache
- **THEN** no further bytes are counted against the budget and the host reader is not called

### Requirement: Explicit file capability
A worker SHALL advertise file support in its ready envelope. A host SHALL refuse a call carrying files before dispatch, with `files-unsupported` and `MayHaveExecuted = false`, in these cases: the worker lacks the capability, the binding is shared or MCP, or the operation is a stream or source. Calls without files SHALL behave exactly as before for old and new workers alike.

#### Scenario: Old worker
- **WHEN** a host invokes with inputs a worker built with an SDK that does not advertise file support
- **THEN** the call is refused with `files-unsupported` before the function runs

#### Scenario: New SDK, old host
- **WHEN** a plugin built with the new SDK opens an input under a host that sends no file descriptors
- **THEN** the SDK refuses locally without sending a frame

#### Scenario: Shared binding
- **WHEN** a caller invokes with files through a shared worker binding
- **THEN** the call is refused with `files-unsupported` before dispatch

### Requirement: Consistent multilingual and remote file calls
The C#, Python and TypeScript SDKs SHALL offer equivalent input and output operations. The same plugin artifact SHALL produce byte-identical outputs under `LocalPluginClient` and through the gateway, where reads and writes are served by the remote caller's reader and destination. Gateway registrations SHALL cap the file limits a credential holder may request, and SHALL permit no files by default.

#### Scenario: Remote round trip
- **WHEN** a remote client calls a function with a 9 MB input and receives a 9 MB output through the gateway
- **THEN** the caller's reader served every block, the caller's writer received and committed the exact output bytes, and no file bytes were persisted by the gateway

#### Scenario: Limit above the registration
- **WHEN** a remote caller requests output limits above the registration's maximum
- **THEN** the call is refused before dispatch

#### Scenario: Python library on a synchronous file
- **WHEN** a Python function passes `as_file()` of an input to a synchronous ZIP reader on a worker thread
- **THEN** the reader works, while the same use on the event-loop thread raises an error instead of hanging
