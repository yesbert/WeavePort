# Platform support and validation

**Windows, Linux and macOS are supported targets for trusted plugin execution over standard input/output (stdio).** Install .NET 10 and the runtimes required by your plugins on an OS/architecture supported by those runtimes. Support here identifies the implemented execution path; it does not claim that the current release has passed validation on every target. Current release validation covers macOS arm64; Windows and Linux release validation is pending.

WeavePort's contracts, tenant binding and protocol are shared .NET code. Process launch, communication, filesystem permissions and resource enforcement are platform-specific concerns. Successful compilation is not runtime, security or performance qualification.

| Environment | Execution and evidence | Remaining limits |
| --- | --- | --- |
| macOS arm64 | Trusted process stdio/socket; qualified packaged multilingual checks; current benchmarks are linked from [status](status.md) | No hostile-code sandbox; unsigned developer bundle; system-wide memory/swap includes background applications |
| Linux arm64, Docker Desktop VM | Historical container and trusted-process comparisons; no current delivered-package qualification | Ordinary children still live inside the trusted coordinator container; not bare metal or independent hardware |
| Windows x64, GitHub-hosted Windows Server 2025 | Native stdio adapter fixtures passed: 38 checks across C#, Python and TypeScript | Current public-package installation, performance and stronger security qualification remain separate |
| Linux x64, GitHub-hosted Ubuntu 24.04 | Native adapter fixtures passed: 38 stdio and 39 socket checks | Current release installation, other distributions/architectures and performance remain separate |
| Other CPU architectures/distributions | Intended platform targets where .NET and required plugin runtimes are available | Not implied by arm64 results |

## Transport and tooling

Stdio is the default on all three operating systems. The optional `UseUnixSocket` profile is supported on Linux and macOS and explicitly rejected on Windows. Windows startup handles its system directory, path separator and workspace creation explicitly. Native workers run with the application's OS-user rights on every platform.

The NuGet libraries are distinct from the example launch scripts and historical offline distribution. Repository Bash runners assume Unix executable and virtual-environment paths; native Windows needs equivalent build steps and Windows interpreter paths. The historical offline bundle targets macOS arm64 and cannot be installed unchanged on Windows or Linux.

The [first native CI run](https://github.com/yesbert/WeavePort/actions/runs/34828099259) passed on Windows x64 and Ubuntu x64 with Python 3.14.7 and Node 24.20.0, using freshly packed source libraries and the existing multilingual adapter fixtures. This is source functional evidence, not installation qualification of every public package or an OS sandbox. [Continuous integration](continuous-integration.md) describes the recurring jobs and retained reports. Windows performance qualification remains open.

## Common acceptance matrix

Each actual target must run the same tenant A/B fixtures for C#, Python and TypeScript: context separation, callbacks, independent workspace/state, crash/hang/cancellation, restart, and cleanup. Capacity evidence includes one outstanding request per active customer, small/large payloads, per-customer tails, all errors, growth, stop reason and recovery. BenchmarkDotNet iteration statistics stay separate from request-level capacity evidence.

Select an explicit supported transport; never silently downgrade a requested security profile. Buffer sizes measured on macOS are workload-specific configuration, not portable defaults. Native execution currently trusts plugin code on every platform: separate processes alone do not establish an adversarial tenant boundary.

## Windows qualification entry point

Use a Windows machine with the repository SDK, Python 3.14+ and Node 24.12+. Keep interpreter paths explicit in a local JSON configuration if `python3`/`node` discovery does not resolve the intended installation. Execute the existing C# consumer, not a reimplementation of its tests:

```powershell
# After packing the three src packages into artifacts/packages and publishing the C# fixture:
dotnet restore tests/WeavePort.Local.Tests --force --no-cache
dotnet build tests/WeavePort.Local.Tests -c Release
# Configure writes fixture paths relative to this checkout; doctor checks actual prerequisites.
dotnet tests/WeavePort.Local.Tests/bin/Release/net10.0/WeavePort.LocalDemo.dll configure . artifacts/local/config.json
$env:WEAVEPORT_LOCAL_TRANSPORT = 'stdio'
Remove-Item Env:WEAVEPORT_LOCAL_SOCKET_BUFFER_BYTES -ErrorAction SilentlyContinue
dotnet tests/WeavePort.Local.Tests/bin/Release/net10.0/WeavePort.LocalDemo.dll verify artifacts/local/config.json artifacts/runs/windows-functional
```

The CI runner uses equivalent build/verification steps with explicit interpreter paths; the PowerShell sequence above has not been independently qualified. Before Windows capacity qualification, add and validate a Windows system memory/commit observer: the current native observer returns unavailable system headroom outside macOS/Linux. A conservative summed-RSS ceiling does not replace that qualification. Evaluate a Windows transport adapter separately if stdio is insufficient; preserve common contracts and multilingual compatibility.

See [local installation](internal-distribution.md), [execution boundaries](local-execution.md) and [platform comparison results (historical) — pre-public record](history.md).
