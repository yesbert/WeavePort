# Matched source streaming diagnostic

This runner reuses `tests/WeavePort.SdkFixture/Workload.cs` for the existing
`list-64k` and `list-8m` workload shapes and full result validation. It measures
three author languages over local and loopback gateway clients, with three warm
calls followed by six iterations of at least 250 ms. Every request is validated.

The delivered-release BenchmarkDotNet harness requires frozen package and fixture
identities. This separate diagnostic permits a source-built before/after comparison
without replacing the shared package feed or candidate qualification artifacts.
It is not a replacement for delivered-release measurements. The gateway runs in
the coordinator process here; the delivered harness uses a separate process.

Build the worker and runner against the desired source root:

```sh
dotnet publish benchmarks/WeavePort.Streaming/Worker -c Release \
  -p:WeavePortSourceRoot=/absolute/source/root -o /absolute/output/worker
dotnet publish benchmarks/WeavePort.Streaming -c Release \
  -p:WeavePortSourceRoot=/absolute/source/root -o /absolute/output/runner
```

Build that source root's TypeScript SDK first with its locked development tools.
Python uses that root's SDK module directly. Then run:

```sh
dotnet /absolute/output/runner/WeavePort.Streaming.dll \
  /absolute/source/root "$PWD/benchmarks/WeavePort.Streaming" \
  /absolute/output/worker/Worker.dll label /absolute/output/results.json
```

Set `WP_STREAM_LANGUAGE=python` for a targeted language repeat. The runner uses
`dotnet`, `python3` and `node` resolved from the invoking process's PATH, then passes
absolute executable paths to the host. Startup is outside warm measurements.
Run comparisons sequentially, retain runtime/binary/source hashes and inspect
both repeated-run variability and the individual measured iterations. Cumulative
host allocations include gateway allocations in gateway mode and exclude worker
allocations; they are not peak memory.
