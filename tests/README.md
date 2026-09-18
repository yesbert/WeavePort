# Verification entry points

`./scripts/verify.sh` qualifies committed HEAD in a fresh checkout with isolated dependency caches. It covers source and packed host contracts, gateway cancellation, documentation, code style, all three applications, author SDK versions, recovery and compatibility. Generated evidence stays in `artifacts/candidates/`.

| Scope | Command |
| --- | --- |
| Host regressions | `dotnet run --project tests/WeavePort.Hosting.Tests -c Release` |
| Windows/Linux CI fixtures | `python scripts/ci/native.py` |
| Native adapter fixtures | `./scripts/verify-local.sh` |
| Composition fixtures | `./scripts/build-bulk.sh`, then `./scripts/bulk.sh verify artifacts/runs/composition` |
| Multilingual SDK fixtures | `./scripts/build-sdk.sh`, then `./scripts/sdk.sh artifacts/runs/sdk verify` |
| Supervised k6 soak | See [multi-hour operation and stop controls](../docs/soak-testing.md) |
| Current performance baseline | See [benchmarking](../docs/benchmarking.md) |
| Optional Docker fixtures | `./scripts/verify-docker.sh` (requires the Docker service and builds fixture images) |

`WeavePort.Local.Tests`, `WeavePort.Docker.Tests`, `WeavePort.Composition.Tests` and `WeavePort.WorkerHost` were moved from the sample tree. Their existing project/assembly names are retained for adapter tooling compatibility. The actual integration templates are [DecisionRoom](../samples/DecisionRoom/README.md), [DocumentWorkshop](../samples/DocumentWorkshop/README.md) and [AppointmentDesk](../samples/AppointmentDesk/README.md).

Capacity, lifecycle, security, installations and load fixtures remain for focused adapter regressions. They do not redefine the current release's platform support or replace its current benchmark protocol. Historical comparator harnesses are available through [Git history](../docs/history.md).

Local-only Docker/capacity/lifecycle/security consumers use `adapter-nuget.config` against the prepared feed, including its reviewed external dependency closure. Prepare core packages and Testing before building those fixtures; this is separate from restoring source-based core regressions.

## MCP checks

Host regressions include optional-MCP malformed-traffic, authority, quotas, cancellation and state-lifecycle checks. [Official SDK interoperability and comparative measurements](mcp/README.md) use an isolated development-only npm fixture. The full candidate verifier exercises its two protocol revisions against the packed host.

## Optional package release qualification

After `./scripts/prepare-core-packages.sh`, run `python3 scripts/verify-optional.py`. This builds fresh-cache NuGet consumers, validates TLS and local/remote composition, checks the optional API/dependency baseline, and executes the multilingual native composition and HTTP delivery fixtures. `--build-only` and `--verify-only` separate preparation from frozen-candidate execution. The clean release candidate uses both phases and hashes the consumer output trees.
