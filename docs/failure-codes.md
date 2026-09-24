# Failure code catalogue

**Release availability:** Structured failure details, diagnostic delivery and combined execution/cleanup metadata are included in .NET 0.8.0 and author SDKs 0.4.0. See the [0.8.0 migration notes](releases.md#080-failure-contracts-and-code-quality) before upgrading from 0.7.0.

Use `Failure.Code` for structured failure identity when present, and the existing status otherwise. Codes identify categories; none guarantees that an external action did not execute. Retain `MayHaveExecuted` and apply an application-owned idempotency or compensation policy before retrying.

| Code | Meaning |
| --- | --- |
| `ok` | Successful legacy status; not a failure code. |
| `failed` | Worker/channel I/O failed or an older peer supplied no detailed code. |
| `sdk-error` | Author handler or callback processing failed. |
| `cleanup-error` | Owned SDK resources could not be released cleanly. |
| `protocol-error` | An explicitly validated protocol boundary rejected malformed input. |
| `internal-error` | Unexpected host state or cancellation without a requested owned token. |
| `cancelled` | Caller cancellation or remote cancellation status. |
| `timeout` | An owned operation/startup/exchange deadline expired, or gRPC reported deadline exceeded. |
| `disabled` | Host/plugin lifetime ended or shared recovery is disabled. |
| `denied` | Callback authority or transport permission refused. |
| `busy` | Admission capacity is unavailable before dispatch. |
| `version-mismatch` | Advertised artifact version differs from the configured version. |
| `input-limit` | Unary request exceeds the contract limit. |
| `value-limit` | Unary response exceeds the contract limit. |
| `stream-limit` | Stream item or total size exceeds the contract limit. |
| `chunk-limit` | Binary source chunk size is outside the contract. |
| `source-unsupported` | The bound client does not support binary sources. |
| `invalid-mode` | Gateway request mode is unsupported. |
| `callback-failed` | Shared host callback processing failed. |
| `binding-denied` | Gateway binding authentication failed. |
| `gateway-failed` | Gateway internal failure or transport data loss. |
| `unavailable` | Remote transport is unavailable. |
| `resource-exhausted` | Remote transport reported capacity exhaustion. |
| `invalid-argument` | Remote transport rejected arguments. |
| `not-found` | Remote transport could not find the requested resource. |
| `unimplemented` | Remote transport does not implement the requested operation. |
| `unknown-error` | Unknown external failure category; descriptions are never parsed as codes. |

`CleanupFailed` records an additional cleanup failure without overwriting the primary code. Phase is `prepare`, `exchange` or `transport`; correlation is an opaque invocation identifier. Old peers may supply neither phase nor correlation. Raw plugin exception details are not sent across the protocol. See [diagnostics](runtime-diagnostics.md) for local opt-in exception delivery.

The [shared contract](../contracts/protocol.json) owns stable wire vocabulary and interoperability limits. Unary values are bounded at 512 KiB; frames at 1 MiB; stream items at 128 KiB; batches at 16 items and 256 KiB; stream totals at 64 MiB. Source requests use 4–256 KiB chunks, defaulting to 64 KiB. These limits bound protocol buffering and preserve interoperable behavior; they are not operating-system memory limits. Concurrency configuration is bounded at 1024 calls. Component-local buffering, retained identity windows and timing policies have separate names and ownership.
