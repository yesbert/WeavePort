## 1. Unified host and ownership

- [x] 1.1 Fold scheduled admission into PluginHost, preserve one budget and migrate existing scheduler consumers.
- [x] 1.2 Add explicit ownership and retention, fair shared/exclusive admission and operation-wide stream residency.
- [x] 1.3 Implement resident shared workers, multiplexed invocation/callback routing, bounded cancellation slots and restart/disabled lifecycle.
- [x] 1.4 Add catalog listing, sealed launch declarations, one approval and direct bound client creation with negative validation tests.

## 2. SDK and composition

- [x] 2.1 Implement protocol 2 concurrent execution, per-invocation cleanup and cancellation in C#, Python and TypeScript; retain serial protocol compatibility.
- [x] 2.2 Implement live stream flushing, heartbeat exchanges and centralized stream deadlines with slow-producer and throughput regressions.
- [x] 2.3 Make callback budget configurable and verify default and raised limits.
- [x] 2.4 Implement bounded source collection and SDK author helpers, verifying 200 MB local and gateway delivery plus cancellation and quota cleanup.

## 3. Evidence and usability

- [x] 3.1 Add executable shared, catalog, live-stream and large-source samples using the simplest public path.
- [x] 3.2 Verify multi-tenant identity, invocation failures, retained cancellation slots, process failure, restart exhaustion, shutdown, fairness and exclusive-mode regressions.
- [x] 3.3 Audit code, APIs, specs, diagnostics, memory ownership and samples; fix findings and preserve audit evidence.
- [x] 3.4 Run reproducible concurrency, streaming and large-source benchmarks; optimize demonstrated costs and record limitations and comparisons.

## 4. Documentation and release

- [x] 4.1 Update all maintained guides, migration documentation, samples, compatibility baselines and generated LLM text.
- [ ] 4.2 Run strict specs, style, documentation, full clean candidate and packed-consumer qualification.
- [ ] 4.3 Bump the complete package family and matching SDK/compatibility inputs, create reviewed PR and merge with green required CI.
- [ ] 4.4 Publish an immutable qualified release through the existing protected workflow, verify all seven public package payloads and retain publication evidence; archive the completed change.
