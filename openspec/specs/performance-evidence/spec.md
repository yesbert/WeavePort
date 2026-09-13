## Purpose
Keep current product performance measurements reproducible, tied to exact artifacts and explicit about their runtime and resource boundaries.

## Requirements

### Requirement: Identifiable current measurements
Benchmark runs SHALL identify the actual core package bytes, optional components, fixture artifacts, source revision, runtime and topology. Functional failures SHALL fail the run rather than contribute successful timing observations.

#### Scenario: Changed package bytes
- **WHEN** a benchmark consumer loads an implementation different from the declared package contents
- **THEN** qualification fails before results are accepted

### Requirement: Complete multilingual paths
The maintained suite SHALL cover embedded and gateway execution of C#, Python and TypeScript providers, including warm small and large results, authorized callbacks and cold lifecycle costs. Reports SHALL name setup and cleanup costs included or excluded.

#### Scenario: Current suite
- **WHEN** the documented complete benchmark command succeeds
- **THEN** all selected combinations have successful complete-result checks and retained timing distributions

### Requirement: Separate request and resource observations
Request-level runs SHALL retain aggregate and per-client outcomes, latency percentiles, duration, concurrency and sampled host/gateway/worker resource scope. Benchmark iteration statistics and host managed allocations SHALL NOT be represented as request tails or total worker memory.

#### Scenario: Unserved or failed client
- **WHEN** any configured client completes no requests or reports errors
- **THEN** the load run fails and retains that client's outcome

#### Scenario: Local topology
- **WHEN** local process and loopback gateway measurements are reported
- **THEN** the report identifies the tested machine and makes no distributed-capacity or hostile-code sandbox claim
