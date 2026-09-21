# From an installation to a client

Use an `InstalledPluginCatalog` to verify releases, one `PluginApproval` to authorize local execution, and a tenant binding to supply application-owned identity and configuration. Local processes run trusted code under the host user's permissions; memory reservations are admission estimates, not hard limits.

A multi-plugin catalog uses this layout:

```text
plugins/
  text-tools/
    active.txt                 # contains 1.0.0
    releases/1.0.0/
      installation.json
      TextTools.dll
```

`catalog.List("text-tools/v1")` returns each matching plugin's verified selected release. `Resolve(plugin, version, contract, savedIdentity)` restores an exact digest pin. The original single-plugin `releases/version` layout remains supported by `Resolve`. Changing `active.txt` affects new selections only. Every runtime alias declared by the manifest must exist in the operator's approved runtime map and pass its hash check; unused approved aliases do not invalidate a plugin.

```csharp
var catalog = new InstalledPluginCatalog(pluginRoot,
    new Dictionary<string, string> { ["dotnet"] = dotnetExecutable });
InstalledPlugin installation = catalog.List("text-tools/v1").Single();
var approval = new PluginApproval
{
    TrustedCode = true,
    MaximumMemoryMiB = 512,
    InvocationTimeout = TimeSpan.FromSeconds(20),
    Streams = new PluginStreamOptions
    {
        ExchangeTimeout = TimeSpan.FromSeconds(15),
        TotalTimeout = TimeSpan.FromMinutes(10)
    }
};
await using IBoundPluginClient client = await host.BindAsync(installation, approval,
    new TenantBinding(authenticatedTenant, configuration), callbacks, grants);
var result = await client.CallAsync("describe", input, cancellationToken);
```

The installation declares a `Launch` object with `Runtime`, literal `Arguments` following the verified entry point, `MemoryMiB`, supported `Ownership` values, and `MaximumDegree`. A Shared launch uses protocol 2 and declares only Shared ownership; exclusive launches use protocol 1. These declarations describe requirements; they never grant permission. An approval that exceeds declared concurrency or chooses unsupported ownership is rejected. The installation supplies plugin, version and profile identity, so callers do not repeat them.

The sealing script accepts an optional `launch.json` inside the release. It copies that reviewed object into the manifest and includes the input file in bundle integrity checks. Without it, the script declares a customer-bound .NET entry point with no arguments and a 256 MiB reservation.

For a reviewed shared plugin, use `Ownership = WorkerReusePolicy.Shared`, select `Degree` and `Workers`, then call `host.ShareAsync(installation, approval, instanceConfiguration, callbacks, grants)`. Its `For(authenticatedTenant)` method returns a tenant-bound client. Shared configuration is instance-wide: do not put tenant credentials there. Shared bindings support unary calls; streams and binary sources are refused before dispatch.

Exclusive streams hold worker residency across start, reads, consumer pauses and cleanup. Empty unfinished batches are valid heartbeats. `PluginStreamOptions` supplies a deadline for each exchange and another for the complete enumeration. Dispose enumeration when stopping early. `SourceAsync` similarly yields bounded binary blocks under one residency lease; use `Composition.CollectAsync` when application code needs a committed, scoped result handle rather than streaming the bytes directly.

`PluginApproval.WorkClass` selects normal or heavy scheduling using the same host admission limits. `PluginApproval` also bounds shared recovery through `MaximumRestarts`, `RestartWindow`, `MaximumAbandonedCalls`, `CancellationGrace` and `SilenceTimeout`. Configure these on the same approval as ownership and concurrency; installation code cannot raise them.

Raw stderr is discarded by default. `ForwardStandardError = true` explicitly allows diagnostic content into the caller's `ILogger<PluginHost>` as event 1007. This content can contain tenant data. Each line is truncated to 4096 characters and the host keeps at most 128 queued lines, dropping older queued lines under pressure. Delivery is best-effort; a failing logger disables this optional delivery. A blocked logger cannot stop pipe draining or host shutdown, but its one background delivery task may outlive the host until the caller's logger returns.
