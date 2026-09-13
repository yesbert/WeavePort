## Context

The native macOS and Linux process/container experiments were completed before the public source baseline. Their results depend on workload, runtime versions, topology and memory-pressure controls. Native Windows qualification remains unavailable.

## Decisions

- Record OS, architecture, SDK/runtime, payload, customer count, source identity and exact artifacts for each run.
- Distinguish admission estimates and summed process RSS from physical-memory usage or enforced resource ceilings. Shared pages can be counted repeatedly in summed RSS.
- Compare matched binaries and workloads, reverse experiment order and retain failures. Linux process children inside a coordinator container are not equivalent to bare-metal Linux.
- Preserve container resource/security policies and identify their differences from trusted native processes. Do not attribute all measured differences to container overhead.
- Preserve system-headroom, swap-growth, all-served and zero-error criteria. A stopped exploration or failing quality threshold does not prove an absolute platform maximum.
- Keep performance conclusions separate from functional correctness and isolation guarantees. Cross-compilation is not Windows runtime qualification.
- Preserve the unfinished Windows task and use docs/platform-qualification.md for required evidence. Do not install or reconfigure unrelated infrastructure implicitly.

## Historical outcome and limitations

Matched Linux runs and repeated native macOS controls completed with cleanup evidence. Native capacity exploration showed workload-dependent limits and non-monotonic failures. The original records and rejected alternatives remain pre-public history; current public claims are limited to evidence retained in the maintained platform and measurement documentation. None of those experiments completes the pending Windows task.

## Sources

- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.workingset64?view=net-10.0
- https://docs.docker.com/engine/containers/resource_constraints/
