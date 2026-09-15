# MCP interoperability fixtures

Development-only official MCP server fixture, pinned to TypeScript SDK 2.0.0. It exposes normalize, echo and test-only process diagnostics without a model or HTTP listener. The production host does not depend on these npm packages.

From the repository root:

```sh
npm ci --prefix tests/mcp --ignore-scripts
dotnet run --project tests/WeavePort.Hosting.Tests -c Release -- \
  --mcp-interop "$(command -v node)" "$PWD/tests/mcp/server.mjs"
```

This verifies both explicitly configured revisions against the official server. `./scripts/verify.sh` repeats interoperability through the packed host. Ordinary host regressions also run deterministic hostile-message and lifecycle fixtures with no npm prerequisite.

A bounded comparative measurement also needs the existing native TypeScript SDK built:

```sh
npm ci --prefix sdks/typescript --ignore-scripts
npm run build --prefix sdks/typescript
dotnet run --project tests/WeavePort.Hosting.Tests -c Release -- \
  --mcp-benchmark "$(command -v node)" "$PWD/tests/mcp" \
  "$PWD/artifacts/mcp-measurements/results.json"
```

Five alternating repetitions cover three protocols and 16/65,536-character echo inputs. Each process has 30 warmups per size, then 1,000/200 measured calls. Timings include caller JSON construction, complete response and validation; allocations include all managed activity in the coordinator during the measurement. Startup is the first complete call after binding, not pure OS process-start latency. SDK implementations and envelopes differ, so this is not pure transport cost or a maximum-throughput test. Failures abort without writing a successful result; retain console logs alongside evidence.

The exact dependency closure is recorded in package-lock.json. MCP package metadata says MIT, while actual SDK 2.0.0 LICENSE files describe an MIT/Apache-2.0 transition and separate documentation terms. Inspect the shipped notices before redistribution; do not infer the license solely from package metadata. Test dependencies are not bundled into WeavePort packages.
