# Shared execution qualification

This executable exercises the public host/client API against real Python, TypeScript and C# worker processes. It requires the TypeScript SDK build and the C# concurrent SDK fixture:

```sh
npm --prefix sdks/typescript ci
npm --prefix sdks/typescript run build
dotnet build tests/WeavePort.ConcurrentSdkTests -c Release
dotnet run --project tests/WeavePort.Shared.Tests -c Release -- "$PWD"
```

Optional positional arguments after the repository root specify absolute Python and Node executables. Test-only executable discovery otherwise uses PATH; application profiles always receive explicit absolute paths. No Docker services are changed.

Coverage includes:

- Sixteen tenant invocations held at a callback barrier, proving actual overlap and correct tenant authority without a speed threshold.
- Independent callback budgets, denied/throwing callbacks, default eight versus approved 64 callbacks, and author failure isolation.
- Cancellation that returns promptly while uncooperative work retains its degree slot, then releases on terminal acknowledgement.
- Detached host callbacks retaining tenant callback capacity until they actually finish, without blocking another tenant.
- Cancellation grace, silence, cleanup failure and process crashes retiring the channel; bounded automatic replacement and disabled restart exhaustion.
- Malformed JSON and unknown identities; shared stream/ordinary Bind refusal; exclusive coexistence and a common worker budget.
- Duplicate callback identities retire the channel; a 10,000-frame callback flood remains within its eight-callback budget. A 512-tenant churn run releases both public admission records and the private fairness index.
- Active and idempotent shutdown, and a worker that stops reading stdin while the host writes 500 KiB: cancellation must return within two seconds despite a one-minute silence policy.

For packed qualification, publish with `-p:UsePackedCore=true` using the candidate feed. The project takes package versions from the repository `Version` property. Set `WP_SHARED_PYTHON_SDK=-` and pass the installed wheel's venv Python to avoid importing source. Set `WP_SHARED_NODE_SDK` to the installed npm artifact's absolute `dist/index.js`, and `WP_SHARED_CSHARP_FIXTURE` to the packed C# fixture assembly. The harness translates SDK selection into explicit worker command arguments because worker environment variables are intentionally filtered.
