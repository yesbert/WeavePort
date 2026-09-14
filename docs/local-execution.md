# Trusted local process execution

Windows, Linux and macOS are supported targets for this trusted process execution path over stdio. Current release validation covers macOS arm64; Windows and Linux release validation is pending. See [platform support and validation](platform-qualification.md) for transport, tooling and evidence limits.


Run your approved plugin as a local process and keep execution policy in the host. Native workers are trusted code with the host OS user’s rights.

The common `ExecutionProfile` feeds one shared binding, invocation, callback, admission and worker-pool implementation. `DockerProfile` and `ProcessProfile` select built-in lifecycle adapters. Choosing a process profile does not resolve an image or execute Docker. Both adapters currently ship in `WeavePort.Hosting`; a separate adapter package/public third-party runtime SDK is not implemented yet.

```csharp
await using var host = new PluginHost();
var profile = new ProcessProfile(
    executable: "/absolute/path/python3",
    arguments: ["-I", "-u", "/absolute/path/plugin.py"],
    trustedCode: true,
    timeout: TimeSpan.FromSeconds(5));

await using var session = await host.BindAsync(context, profile, callbacks,
    grants: ["documents.read"]);
```

For native socket execution on macOS/Linux, set `UseUnixSocket = true` on the process profile. This is opt-in; stdio remains the default. Each worker owns a short private temporary socket directory (0700, socket 0600), separate from its cooperative workspace so macOS socket path limits do not depend on installation-path length. `SocketBufferBytes` optionally requests 4 KiB–1 MiB host kernel buffers per direction; it requires socket mode and does not alter system-wide settings or worker runtimes. Kernel buffers are outside managed allocation/RSS accounting. The local tools also accept `WEAVEPORT_LOCAL_SOCKET_BUFFER_BYTES`. Startup has the same deadline and no silent fallback; cleanup removes its endpoint or retains uncertain accounting. Same-user code is still trusted. The local tools accept `WEAVEPORT_LOCAL_TRANSPORT=stdio|socket` or `UseUnixSocket` in their JSON configuration.

Set `requiredProtection` on `BindAsync` or `PrewarmAsync` when an application requires `RestrictedFileSystem`, `DisabledNetwork` or `HardResourceLimits`. A process profile provides none and fails before launch. Docker profiles request these restrictions from Docker, subject to the deployment trust and effective-policy limitations in [security architecture](security-architecture.md). These flags are not kernel-exploit attestations. The application must derive required protection from trusted installation policy, never from a plugin request. There is no automatic downgrade or fallback between adapters.

## Local guarantees and limits

- Explicit trusted-code acknowledgement is required. Local plugins and their libraries run with the application's OS-user rights; a separate process/workspace is not a hostile-code sandbox.
- Launch profiles must be customer-neutral; never embed customer credentials or context in command arguments or baked-in runtime files. Arguments are frozen at construction and passed with `ProcessStartInfo.ArgumentList`, not shell interpolation. Executables use absolute paths. The operator must keep executable/script trees stable and trusted; this does not reproduce immutable Docker image content resolution.
- The launch environment is cleared, then populated with a minimal executable/system PATH, private HOME/temp/workspace paths and explicit runtime flags. Parent credentials and options such as NODE_OPTIONS/PYTHONPATH are not implicitly inherited. Complex runtimes/dependencies may require future explicit environment policy.
- `WEAVEPORT_WORKSPACE` tells cooperative fixtures where to store instance state. It is not a filesystem jail. Default Docker fixtures retain `/tmp` when the variable is absent.
- The same bound authority, frame/depth limits, duplicate-envelope validation, callback budgets, cancellation, admission and idle policy apply. Granting a callback never replaces object-level authorization inside the callback.
- Pristine profiles are compared by frozen launch choices and version. Equivalent profiles share the reserve; used roots and workspaces are never assigned to another customer. Idle release/restart creates fresh state.
- `reservedMemoryMiB` contributes to shared/per-tenant admission accounting only. No hard native CPU, memory or PID ceiling is implemented. Do not run hostile or unlimited memory-fault operations in this profile.
- Stop uses best-effort process-tree termination and a bounded root/drain wait. A successful root exit does not establish that every detached descendant is gone. Cleanup failures remain reserved/quarantined. Supported local fixtures must keep child lifetimes cooperative and must not daemonize. The verified fixtures do not daemonize; a robust OS process-ownership/sandbox adapter remains future work.

Instance strings include the native PID for diagnostics and the local load observer. They are not authority tokens; never authorize a call or kill a process solely from a client-supplied instance string.


## Installation and measurement

See [installation](internal-distribution.md) for the portable self-contained macOS output and language-runtime prerequisites. [Local results (historical) — pre-public record](history.md) records actual functional, BenchmarkDotNet and capacity evidence.

The current local capacity executable offers one outstanding call per customer, 16 or 65,536 ASCII payload characters plus JSON envelopes, and a 20-second default active window. It records exact retained request durations, status counts and per-customer p99, with an eight-million-observation budget per stage. A customer reaching its share of that budget stops the experiment; it is not a statistically sampled p99.

The observer samples root-process RSS and macOS-reported system memory-free percentage approximately once a second. RSS sums may double-count shared pages; they exclude descendants and are not directly comparable to Docker cgroup memory. The host contains the load generator and measurement state. Guards stop growth for host RSS above 2 GiB, summed host/worker RSS above 12 GiB, system free percentage below 15%, observer failure or per-customer quality failure. The native memory reservation is not used as evidence of enforcement. This is a bounded local fixture experiment, not a native SaaS density guarantee.

Sources: [Process.Kill descendant limitation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill?view=net-10.0), [ArgumentList](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.argumentlist?view=net-10.0).

## Next security adapter work

Evaluate OS-specific confinement and process ownership separately for macOS, Linux and Windows: effective filesystem/network policy, kernel/runtime attack surface, descendant lifetime, installation privileges and a signed/updateable distribution. A local process adapter cannot satisfy those requirements through a boolean trust label. A VM-backed local service is another candidate whose installation, IPC and memory cost needs testing. None is selected or implemented here.

The proposed lifecycle broker must speak in execution profiles and lease generations, not arbitrary Docker commands. Its independent authorization and reconciliation work remains useful for both container and stronger non-Docker adapters. This trusted local milestone does not complete that broker.
