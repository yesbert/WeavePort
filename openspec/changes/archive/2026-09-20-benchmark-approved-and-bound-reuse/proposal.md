# Benchmark approved-session and customer-bound reuse

## Why
The agreed deployment policy has two modes: unreviewed plugins retain containers only for the same customer/plugin/version; approved plugins may reuse a compatible interpreter after cooperative session cleanup, accepting hidden-state risk.

## What Changes
Add an experimental affinity-aware pool, periodic low-frequency customer replay, registered-session workload and targeted boundary checks. Compare approved cross-customer reuse with bound reuse and eviction. No public SDK or production hosting behavior changes.

## Impact
Python benchmark tooling and retained benchmark evidence only. Existing native-host and forkserver results remain historical controls; they are not the implementation of the agreed policy.
