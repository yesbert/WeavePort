## Why

Keeping one process alive for every registered customer-plugin pair ties server memory to customer count. Existing fail-fast per-tenant limits cannot lend otherwise unused capacity fairly or distinguish short normal work from authorized heavy jobs.

## What Changes

- Add an opt-in shared scheduler with bounded admission queues, single-flight customer-plugin registrations, least-active-tenant selection and round-robin tie breaking.
- Evict idle workers under shared count/memory pressure; preserve exclusive tenant ownership and existing direct-host behavior.
- Apply separately configured normal/heavy deadlines and heavy declaration, concurrency and memory restrictions.
- Adapt a bounded pristine reserve to recently requested registered execution profiles; no speculative reserve for absent languages.
- Add public API regressions and a bounded, supervised density staircase with closed-loop and fixed-arrival workloads.
- Record initial MacBook Air results independently of historical M2 Ultra measurements, then pause boundary expansion for review.

## Capabilities

### New Capabilities
- `fair-scheduling`: Fair admission, reconstructible residency and bounded heavy work.

### Modified Capabilities
- None; direct `PluginHost` admission remains unchanged.

## Impact

Additive Hosting API, developer guidance, scheduler tests and density tools. No dependency changes, releases, distributed scheduling, cross-tenant used-worker reuse, safe native sandbox claims or changes to unrelated services. Existing historical platform qualification remains separate. No old sources are retired.
