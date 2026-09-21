# One installed worker, two tenants

This C# example discovers a verified installation, approves one resident shared process with degree four, and invokes it concurrently through two tenant-bound clients. It loads no real model and makes no CPU throughput claim.

After preparing the matching local package feed, run from the repository root:

```sh
dotnet publish examples/shared/SharedExample.csproj -c Release -o artifacts/shared-example/plugins/shared-example/releases/1
```

Create `artifacts/shared-example/plugins/shared-example/releases/1/launch.json`:

```json
{
  "Runtime": "dotnet",
  "Arguments": ["--worker"],
  "MemoryMiB": 256,
  "Ownership": ["Shared"],
  "MaximumDegree": 4
}
```

Seal the inactive installation, select it, and run the host (use your absolute .NET executable path):

```sh
python3 scripts/seal-installation.py artifacts/shared-example/plugins/shared-example shared-example 1 shared-example/v1 SharedExample.dll /absolute/path/to/dotnet
printf '1\n' > artifacts/shared-example/plugins/shared-example/active.txt
dotnet artifacts/shared-example/plugins/shared-example/releases/1/SharedExample.dll artifacts/shared-example/plugins /absolute/path/to/dotnet
```

Expected output includes `alice: HELLO`, `bob: WORLD`, and one ready shared worker. Do not modify or rebuild the installation while it is running. Shared process memory is a trusted-code boundary; configuration is instance-wide and must not contain tenant credentials. The example grants no callbacks.
