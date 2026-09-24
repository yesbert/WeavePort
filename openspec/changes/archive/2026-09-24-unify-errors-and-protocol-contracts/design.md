## Context
The host owns failure classification. Exception messages remain human-readable and never serve as protocol identifiers. Existing status, MayHaveExecuted and version-mismatch information remain available.

## Decisions
Use a generated internal vocabulary with responsibility-specific groups. A JSON contract owns shared operations, frame kinds, error codes and byte/count limits; component-local policy remains local. Generated outputs are checked, and independent fixtures retain literal expectations. Do not extract ordinary empty values, arithmetic identities or diagnostic prose.

Add safe failure metadata (code, phase and correlation ID) alongside existing status, preserving old peer compatibility. Cleanup failures must not overwrite the primary failure; aggregate local exceptions and identify cleanup separately. Default logs omit exception messages. A caller-owned opt-in sink receives full exceptions and is responsible for access controls and retention. A diagnostic sink failure must not replace an operation outcome.

Cancellation classification checks the caller, host lifetime and actual deadline, in that order. Unrelated cancellation is an internal failure. InvalidOperationException and KeyNotFoundException are not globally treated as protocol failures; protocol boundary validation owns malformed-input conversion. No retries are inferred from any failure code.

Transport status detail is untrusted prose. gRPC status categories map to fixed codes; platform codes and safe metadata use separate fields/trailers with controlled unknown fallbacks. Public constructors are retained and metadata is additive.

## Alternatives
A global Constants class obscures ownership. Replacing every literal obscures simple code. A global first-chance exception logger leaks information and duplicates recovered errors. Neither approach is used.

## References
- https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
- https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation
- https://learn.microsoft.com/en-us/aspnet/core/grpc/error-handling?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/constants
- https://www.typescriptlang.org/docs/handbook/2/narrowing.html

## Verification
Focused failure/cleanup/cancellation/transport regressions, independent protocol-value assertions, generation drift check, existing readability and architecture gates, and isolated native package qualification. Docker services remain untouched. Record actual results in verification.md before archival.

## Implementation review

The existing invocation event 1003 now carries code and cleanup outcome, avoiding a second classification log for the same boundary. Event 1008 reports failures of the opt-in diagnostic destination without its message. Shared invocations also mark trace failures. Startup deadlines are distinguished from the longer invocation deadline, and startup teardown retains primary plus cleanup causes.

Python retains the public `SessionCleanupError` category and adds an immutable `errors` tuple while preserving the existing first-cause chain. This avoids changing callers' exception catches to an exception-group category. TypeScript uses discriminated inbound frame unions and validates parsed `unknown` values before routing. Generic author handler inputs retain the existing public compatibility surface; wire routing no longer relies on `any`.

Additional official references reviewed during implementation:
- https://docs.python.org/3.11/library/exceptions.html#exception-groups
- https://www.typescriptlang.org/docs/handbook/2/narrowing

Detailed diagnostics remain local to the host and cannot manufacture plugin stack traces from safe remote error frames. Legacy operation status remains available alongside the more specific failure code; additive protocol fields allow old peers to retain their original behavior.
