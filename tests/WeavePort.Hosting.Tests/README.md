# Hosting verification

Run the complete native suite with `dotnet run --project tests/WeavePort.Hosting.Tests -c Release` from the repository root. `Program.cs` routes commands and invokes the scenario groups.

| Directory | What it verifies |
| --- | --- |
| `Protocol/` | Independent envelope values, fragmented frames, quotas, cancellation and transport diagnostics |
| `Installations/` | Sealed artifact identity, discovery refusals, ecosystem requirements and bounded runtime probes |
| `Lifecycle/` | Admission, disposal, lock ordering, quarantine and safe failure diagnostics |
| `Transports/` | Owned process sockets, Linux worker endpoints and Docker CLI argument boundaries |
| `Mcp/` | MCP protocol revisions, hostile messages, authority, lifecycle and optional interop measurements |
| `fixtures/` | Independent manifest pins and generated ecosystem oracle cases copied to the test output |

The default suite uses native processes and a synthetic Docker executable; it does not need a Docker daemon. Platform-specific checks retain their operating-system guards. The deliberately faulty peers are verification inputs, not application templates. The MCP measurement command is separate from the functional suite and must not be described as a capacity benchmark.
