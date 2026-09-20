# Design

Operator-owned ExecutionProfile.ReusePolicy defaults to CustomerBound. ApprovedSessions requires an additive sessionCleanup=1 ready capability and a reusable boolean on native result envelopes. MCP cannot opt in. A successful unary SDK call cleans registered resources before reusable=true. Streams report false until close/end and retain their original worker/context. Host deadlines cover cleanup; errors, invalid acknowledgements and cancellation destroy the worker.

Approved clean workers form a separate available set from pristine workers, keyed by normalized resolved profile plus artifact version. Policy, immutable Docker image or trusted local executable/arguments and adapter resources remain part of compatibility. Plugin identifiers can differ only when they use the same compatible deployment; this does not dynamically load arbitrary plugin packages. Defaults preserve independent customer-bound state. Local execution remains trusted same-user execution.

Registered cleanup runs in reverse order, attempts every action, revokes context callbacks before cleanup, clears owned references and fails closed. The SDK does not claim to erase arbitrary globals, native-library state, credentials in copies, process-wide environment mutations, files outside registered ownership or unregistered background work. Authors must explicitly own these resources and await work before return. Cleanup failures are not successful plugin results.

A host-clean worker has no tenant assignment and remains counted against global reservations. Idle reusable workers expire independently from pristine targets and can be reclaimed for incompatible demand. Scheduler resident accounting reflects whether a binding still owns a worker. Queue/fairness and heavy admission remain unchanged.

Verification uses the installed .NET SDK and existing native JSON envelope/SDK tests; no new runtime API dependency is required beyond IAsyncDisposable, ValueTask and existing cancellation primitives. No forced GC or language-specific heap reset is used. Product performance must be measured through native host/SDK envelopes rather than transferred from Python fixture results.


The final API review retains the published eight-argument WorkerPoolOptions constructor and deconstruction. WaitForStartCapacity and ReusableIdleTimeout are additive init properties. This also removes an unnecessary positional-constructor break from the preceding scheduler candidate. Package labels are unchanged development labels, not a published feature release.
