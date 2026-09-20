# Dormant customer registry diagnostic

This source-built diagnostic holds four warmed native C# workers constant and increases dormant customer registrations. It isolates coordinator overhead and registration memory from process/container startup. Its result is **requests/second for four hot customers**, not distinct customers/second or a Docker capacity promise.

```sh
dotnet build benchmarks/WeavePort.Registry -c Release
dotnet benchmarks/WeavePort.Registry/bin/Release/net10.0/WeavePort.Registry.dll 10000 /absolute/path/to/result
```

Each measured request validates its tenant. Registration, a 15-second hot interval and disposal are measured separately. The report includes four per-customer counts, managed registration memory after collection, root-process RSS, the exact Hosting DLL hash and final cleanup counters. Up to 100,000 registrations can be explicitly configured for this experiment; the public scheduler's default registry admission bound is unchanged. Run timings separately from builds and other load tests.
