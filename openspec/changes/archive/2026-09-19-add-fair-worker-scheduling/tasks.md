## 1. Implementation
- [x] 1.1 Implement additive scheduler, immutable unique registrations, bounded queues and dynamic tenant admission.
- [x] 1.2 Implement pressure/idle eviction, demand-derived shared reserve, normal/heavy budgets and cleanup accounting.
- [x] 1.3 Verify public contracts using source and packed consumers, including fairness, authority, failure and shutdown.

## 2. Measurement
- [x] 2.1 Implement bounded closed-loop and fixed-arrival density measurement with per-tenant latency/outcomes and resource guards.
- [x] 2.2 Verify harness controls and run the initial MacBook Air native/Docker pilot; retain passing and failing evidence separately from historical baselines.
- [x] 2.3 Run a small fixed-arrival control to demonstrate latency includes admission waiting and generator lateness.
- [x] 2.4 Run the clarified many-customer/low-frequency population pilot, with no warmup, recorded per-customer demand and first-failure stopping.

## 3. Documentation and review
- [x] 3.1 Document reconstructible state, concurrency, heavy authorization, queue/operation deadlines and capacity procedure.
- [x] 3.2 Run code style, documentation, OpenSpec and relevant regressions; synchronize verified specifications.
- [x] 3.3 Present initial results and leave larger boundary refinement/soak for user discussion. The user subsequently authorized expanded security/capacity testing and optimization; continuation is tracked in `qualify-reuse-security-and-capacity`.

Verification note: source and locally packed scheduler consumers now pass 88 public assertions, including deterministic pristine-startup admission regression checks; packed hosting regressions pass with the same Hosting DLL identity. Initial native population results and failed Docker observer stages are retained. Final candidate qualification of committed HEAD and release publication have not run. The initial memory-led revision additionally retains workers across two once-per-minute periods; those initial observations are not a maximum-capacity or soak qualification.

Retained first memory-led observations: `reports/benchmarks/fair-scheduling-air-20260919/`. Native passes with 100 resident workers and two calls per customer; the original Docker series is invalid because its memory observer timed out. The subsequent qualification change repairs sampling, retains separate valid Docker controls and fixes an actual pristine-reserve admission race without changing used-worker customer ownership. Verified scheduler requirements, including pending shared startup behavior, are synchronized.
