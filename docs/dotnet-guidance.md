# Framework and memory guidance

Reviewed 2026-09-10. The repository currently resolves SDK 10.0.401, target `net10.0` and C# 14.0. Recheck rather than treating this snapshot as a permanent version mandate:

```sh
dotnet --version
dotnet msbuild src/WeavePort.Hosting -getProperty:TargetFramework,LangVersion
```

Use the language version supplied by the target framework unless a scoped change justifies an explicit override. Do not select `latest` or preview to obtain syntax incidentally. Framework upgrades and syntax changes do not themselves prove lower resource consumption.

## Reference documentation

Verify API availability against the selected SDK and current [Microsoft .NET documentation](https://learn.microsoft.com/en-us/dotnet/). Record relevant technical sources in change designs. Avoid assuming that preview documentation applies to this repository's target framework.

## Memory ownership rules

Use `ReadOnlySpan<T>`/`Span<T>` for synchronous views where they avoid copying. A span cannot remain live across an `await`; use `ReadOnlyMemory<T>`/`Memory<T>` and an explicit owner for asynchronous lifetimes. Cancellation requested is not proof that a consumer has stopped touching its memory. These rules follow the [Microsoft memory guidelines](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines).

Pooling buffers requires a single owner, a defined logical length and exactly one return after all consumers finish. Never expose unused capacity, retain a slice after return or assume rented memory is zeroed. Clear sensitive owned byte regions before returning them, using `CryptographicOperations.ZeroMemory` where appropriate; `ArrayPool.Return(clearArray: true)` alone is not a universal erasure guarantee. Immutable strings and copies require separate lifetime analysis. See [ArrayPool.Return](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1.return?view=net-10.0) and [ZeroMemory](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory?view=net-10.0).

`Span<T>` is a memory view, not a tenant-isolation mechanism. `ObjectPool<T>` limits retained objects rather than total allocations; it cannot enforce worker admission or memory budgets. Pooling also retains memory and may cost more than allocation. See [Microsoft's object-pool guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/objectpool?view=aspnetcore-10.0).

## Performance changes

Measure complete SDK operations and managed allocations with the [current benchmark suite](benchmarking.md). Keep individual-request tails and sampled process memory separate from BenchmarkDotNet iteration statistics. Allocation reductions in the coordinator do not prove reduced worker memory. Earlier transport and GC experiments are described in [historical evidence](history.md).

## Coordinator GC configuration

Record `GCSettings.IsServerGC`, `GC.GetConfigurationVariables()` and processor count rather than inferring the active collector from a project name. Compare throughput, request tails, cumulative GC pause and actual container memory together. A mode called server GC is not automatically the best choice for a coordinator sharing CPUs with many isolated workers. GC settings belong to the consuming process/deployment, never to a library's global initialization.

Re-measure on each deployment and runtime patch before selecting a fixed heap budget. Sources: [GC configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector), [DATAS](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/datas).
