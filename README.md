# WeavePort

An embedded backend plugin platform for owner-controlled applications and plugins. Applications own their domain contracts, authorization and durable state; WeavePort owns bound execution, callbacks, admission and worker lifecycle.

The current internal distribution is **0.1.0-internal.2**, qualified for native macOS arm64. It is not a public stable release or a hostile-plugin sandbox. See [current status and open work](docs/status.md), [architecture](docs/architecture.md) and the [integration contract](docs/v1-integration-contract.md).

## Start here

- [GitHub and NuGet delivery](docs/releases.md) and [AI/MCP documentation retrieval](docs/ai-documentation.md).
- [Install the internal distribution](docs/internal-distribution.md): offline packages, executable examples and copyable templates.
- [Author plugins in C#, Python or TypeScript](docs/plugin-sdk.md).
- [Embed one coordinator](docs/embedded-coordinator.md), [select installed artifacts](docs/installed-plugins.md) and [handle recovery](docs/native-operations.md).
- [Exact package/API compatibility](docs/package-compatibility.md) and [operational diagnostics](docs/runtime-diagnostics.md).

## Reference applications

| Application | Demonstrates | Build and verify |
|---|---|---|
| [Decision Room](samples/DecisionRoom/README.md) | C#/Python strategies, scoped knowledge and journal replay | `./scripts/decision-room.sh --build --verify` |
| [Document Workshop](samples/DocumentWorkshop/README.md) | Interchangeable readers, bounded source access and staged commits | `./scripts/document-workshop.sh --build --verify` |
| [Appointment Desk](samples/AppointmentDesk/README.md) | Scheduling strategies, idempotent actions and recovery | `./scripts/appointment-desk.sh --build --verify` |

HiveWeaver, TreeWeaver and NextPA remain independent consumers; they are not dependencies and are not modified by these examples.

## Development commands

Use the SDK pinned in `global.json`, Python, Node and the globally installed OpenSpec CLI. The default product workflow needs no Docker service changes.

```sh
./scripts/build.sh                       # Build the three current examples
./scripts/verify.sh                      # Qualify committed HEAD in a fresh checkout
./scripts/benchmark.sh --distribution /absolute/path/to/installed/payload
```

Benchmarking requires the extracted internal.2 bundle or its installed `payload` directory and verifies the exact delivered core packages. See [measurement scope and results](docs/benchmarking.md). Individual working-tree checks and opt-in adapter suites are documented in [tests](tests/README.md).

## Repository map

| Directory | Maintained contents |
|---|---|
| `src/`, `sdks/` | .NET platform packages and language author SDKs |
| `samples/` | Three product applications and shared integration templates |
| `examples/sdk/` | Minimal multilingual SDK providers |
| `tests/` | Regression fixtures, compatibility and deployment checks |
| `benchmarks/` | One multilingual SDK performance suite |
| `tools/`, `scripts/` | Build, qualification, packaging and measurement entry points |
| `docs/`, `openspec/` | Current guidance, contracts and unfinished changes |
| `reports/` | Current release evidence and compact current benchmark results |
| `artifacts/` | Ignored generated binaries, caches and raw runs |

Historical PoC experiments, reports and completed plans are available through the [historical evidence guide](docs/history.md). They are not duplicated in the current tree. Development rules: [CONTRIBUTING.md](CONTRIBUTING.md) and [engineering guidelines](docs/engineering.md).
