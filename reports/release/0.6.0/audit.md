# 0.6.0 implementation audit — working record

This is an independent source and targeted-runtime review of the WPF handoff against the current candidate, not a blanket release pass. The release owner must reconcile pending items with final committed and packed evidence. Source can change during parallel review; paths identify the reviewed responsibility rather than a frozen revision.

## Findings and follow-up

| Priority | Finding | Status / evidence |
|---|---|---|
| P1 | Shared invocation writes used only worker lifetime cancellation; a worker that stops reading stdin could block caller cancellation until the silence watchdog. Cancel-frame writes also started their grace wait too late. | Fixed in `SharedWorker.InvokeAsync`/`CancelAsync`: dispatch uses the invocation token, partial writes retire the channel, and one grace timer covers cancel-frame writer wait and acknowledgement. Real nonreading-worker regression passed with a 500 KiB write, 100 ms caller cancellation and a separate one-minute silence timeout. |
| P1 | Cancellation between scheduler lease admission and underlying operation acquisition could reach the generic scheduler-failure handler, permanently poisoning admission. | Fixed. `OperationLeaseChecks.CancelledAdmissionAsync` exercises repeated immediate cancellation; source run passed 40 immediate-cancellation iterations and a successful subsequent invocation. |
| P1 | Cancellation of a paused stream could release the operation reservation while old iterator cleanup later restarted the binding, potentially disrupting subsequently admitted work. | Fixed by the host owner. All three actual-language stream tests passed paused cancellation, admission release and late old-iterator disposal preserving the newly admitted worker. Packed qualification remains a separate final gate. |
| P2 | Shared ephemeral scheduling registrations retained one fairness turn entry per historical tenant. | Fixed; 512 transient tenants in batches of 16 leave admission records, registrations, queued/active work and the internal fairness index empty. |
| P2 | Shared callback IDs and fire-and-forget replies could accumulate after the invocation callback budget was exhausted. | Fixed with budget-first admission and one rejection reply. Duplicate callback IDs retire the channel; 10,000 callback frames execute at most eight callbacks, fail only that invocation and leave the worker usable. |
| P2 | Binary collection always sent close after a terminal `done` response whose SDK had already closed the source, causing needless healthy-worker retirement. | Fixed: local client skips redundant close after confirmed completion. SDK owner reran 200 MiB local/gateway collection successfully. |
| P2 | Binary client decoded base64 through a large temporary UTF-16 string. | Measured and removed using `JsonElement.TryGetBytesFromBase64`. For one 200 MiB development run, host allocation fell from 1,064,301,704 to 500,690,008 bytes locally and from 1,488,499,976 to 924,398,208 via gateway. Single-run latency improved from 983 to 584 ms and 1336 to 752 ms respectively; these timings are not a robust general speedup claim. |
| P2 | Installed approval exposed shared recovery knobs but initializer initially forwarded only degree/workers. | Corrected initializer forwards restart count/window, abandoned-call count, cancellation grace and silence timeout. Included in final package qualification. |
| P2 | Manifest compatibility originally selected only protocol 1 although shared startup requires protocol 2. | Parser now accepts the embedded supported-protocol set; sealing selects protocol 2 for Shared launches, 1 for exclusive launches. Mixed Shared/exclusive launch declarations are rejected. Included in final compatibility/package qualification. |

## Requested work coverage

| Finding | Implemented route | Evidence and remaining qualification |
|---|---|---|
| WPF-09a | One `PluginHost`, `SchedulingOptions`, common budget, local unary client follows host admission. | Scheduling source suite passed 88 existing assertions; multilingual reuse suite passed 1,036 assertions. Density and Registry consumers compile with new API. |
| WPF-09b | `IPluginOperationSession` and operation-wide exclusive lease for streams/sources. | Raw pressure/consumer-pause, zero-wait and cancelled-admission checks passed. SDK agent also passed actual C#/Python/TypeScript stream residency and late-cleanup tests. |
| WPF-10 | Shared ownership, protocol 2, tenant views, per-invocation callbacks, retained abandoned slots, resident recovery and opt-in stderr. | Host owner owns full three-language failure/concurrency suite and shared benchmark. Bounded stderr regression passed with a blocked logger and flooded pipe; default mode emitted no raw logs and disposal did not wait for the logger. |
| WPF-11 | Contract listing, selected release layout, runtime subset integrity, launch declarations and one binding approval. | Packed C# shared example discovered/sealed/launched one installation and returned `alice: HELLO`, `bob: WORLD`, one resident worker. Existing installation suite extended with subset/discovery/pin/launch/approval guards. Additional packed consumer `MixedLaunchChecks` passed a root containing sealed Python and Node providers, verified runtime subsets, catalog discovery, approved shared tenant calls and refusal without Shared approval; no manual process profiles. |
| WPF-12 | `SourceAsync` on bound local/remote clients and `Composition.CollectAsync` into the existing result scope. | All three SDKs passed byte-for-byte 200 MiB local and gateway collection, quota, cancellation and recovery checks (six lanes). The SDK evidence report includes 10 ms sampled root-process RSS; Python source peak was about 28 MiB. See source-evidence.md for sampling limitations. |
| WPF-01/02 | Immediate available-item stream delivery, heartbeats, separate exchange and total options. | Local fake-session heartbeat/lease checks passed. SDK agent passed actual C#/Python/TypeScript live first-item and 12-second first-item tests on a five-second unary binding, independently verifying the unary timeout. Prior throughput acceptance still belongs to final benchmark evidence. |
| WPF-03 | MaximumCallbacks on profile/approval, default eight. | Shared test suite includes increased callback budget; installation negative test validates zero budget before startup. Actual C#, Python and TypeScript tests passed default ninth-refused and configured forty-of-sixty-four cases. |

Closed or withdrawn WPF items were not reopened as new mechanisms. The ownership setting remains `WorkerReusePolicy` with three values; the suggested rename was optional. Runtime path approval stays in the catalog map so file verification and launch share one path source, while execution policy is in `PluginApproval`. Shared instance configuration is an explicit ShareAsync argument. These are intentional API choices rather than silently missing behavior.

## Documentation and usability

The recommended path is catalog → approval → BindAsync/ShareAsync → bound client. A runnable `examples/shared` sample uses that path without constructing `ProcessProfile`. Maintained architecture, lifecycle, scheduling, SDK, protocol, composition, catalog, compatibility, diagnostics and website concept guides were updated. Searches found the old `ScheduledPluginHost` name only in an explicit migration paragraph in maintained guides. Relative-link and diff-whitespace checks passed during this review. Final version, API baselines, generated LLM files and packed/release evidence remain the release owner's final gate.

Raw stderr remains explicit opt-in because plugin payloads can contain secrets even when shared configuration does not. Delivery truncates lines to 4096 characters and queues at most 128 lines per host. A blocked logger can leave one delivery task alive after host disposal; it cannot block pipe draining or shutdown. The accepted limitation is documented next to the feature.

## Targeted lease test result

`dotnet run --project tests/WeavePort.Scheduling.Tests -c Release` completed successfully after the host cancellation fixes: bounded opt-in stderr; operation residency through idle expiry and pressure; zero-wait refusal; 40 immediate cancelled lease admissions; all 88 existing scheduler assertions. This uses raw lease exchanges to inspect instance/counter retention. The SDK agent separately owns real streaming cleanup and delayed-producer qualification.

## Reviewed package surface

The refreshed seven-package candidate was restored into dedicated empty core/optional review caches and published to separate review output directories. Core and optional API candidates were inspected and their deliberate changes accepted into the reviewed baselines: unified host/Shared types, approval/catalog/lease contracts, source collection, stream options and additive gateway source fields. Internal scheduler/worker/diagnostic implementation types remained non-public. Both packed API baseline gates then passed. Hosting's new Client dependency is expected; it adds no external package/license.

A preexisting ambiguous `RemotePluginClient(endpoint, credential, null)` overload was exposed by the new packed consumer check. The transport-options overload now requires its fourth `callTimeout` argument; the simple nullable-timeout overload remains unambiguous. Transport call sites were migrated. Package documentation validation also caught a new relative README link whose target is outside the NuGet artifact; that link was changed to its public documentation URL and requires the final repack.

Mixed catalog acceptance was executed with `dotnet run --project tests/installations -c Release -p:RestorePackagesPath=artifacts/mixed-installation-test/packages -- <repo> <dotnet> --mixed-only`, using packed 0.6.0 Hosting/Client and checked-in 0.3.0 author SDK sources/build output. Final isolated candidate qualification must repeat it alongside the complete installation guard suite.

## Final focused follow-up

The Hosting regression suite passed after adding mixed exclusive/shared quarantine accounting checks: live reservations are counted once, a quarantined shared worker leaves the live Shared counters, quarantine memory includes both ownership modes, and confirmed cleanup zeros every reservation category (10 quarantine assertions total). The release export unit suite passed all 12 cases; documentation links and the public-tree check also passed.

Two clean-candidate integration errors were reported before final qualification: the shared sample stage omitted its required catalog/dotnet arguments and preparation, and multilingual stream/collection tests selected PATH Python while expecting imports from the separately installed SDK virtual environment. These require the release owner's candidate-runner correction and a complete clean run. Current package README and website release/version statements were aligned with HostApi 2, exclusive/shared protocols 1/2, author SDK 0.3.0 and seven .NET packages. Generated LLM output must be regenerated after these source-document edits; historical evidence remains version-specific.

## Final source integration

The complete solution builds with zero warnings/errors. Strict OpenSpec validation passes with nine capabilities synchronized to the verified implementation. The candidate runner now publishes and seals the shared example before calling it with catalog/runtime arguments, uses the installed wheel interpreter for every language integration lane, selects the matching frozen C# fixture, and inventories both new samples. Gateway pause tests for each language verify that caller cancellation and total deadlines release server admission before the old iterator is disposed; a later call survives that old disposal. The static website includes the new guides and both samples in navigation and its self-contained LLM resources. Final clean-checkout and public-release evidence will be linked separately.

## Throughput and immediate-callback correction

The matched source benchmark reproduced a Python stream regression from scheduling one task per item. A bounded retained producer replaces that overhead while preserving one generator advancement, frame/item/total bounds and prompt heartbeat delivery. A separate direct-callback regression exposed prefetched callbacks that could delay an already available item in all three SDKs; callbacks now wait for the next exchange when a batch is ready. Dedicated wire tests check both before-first-item callbacks and the new exchange identity. Raw before/after measurement and final qualification remain separate evidence.

The first isolated candidate completed all functional stages but correctly refused final qualification because the MCP example lockfile still declared 0.5.0 and lacked Hosting’s new Client edge. Its refreshed 0.6.0 dependency graph is now tracked; the gate was preserved. Coverage execution now includes the real shared and source/stream suites rather than relying only on the older host suites.

## Post-merge shutdown and callback audit

Candidate `201fbd6` passed the complete isolated gate with 3,526 reported assertions and 364 frozen files. PR #32 passed required checks and merged as `7ed8948`; main CI and CodeQL passed. Main coverage stalled in the shared suite and was cancelled for diagnosis. Ten repeated Debug shared suites and eighteen additional C# wire/collection runs passed; without a thread dump the precise CI stall cannot be attributed conclusively.

Lock-order review nevertheless found a concrete host-disposal inversion: synchronous progress into scheduler disposal while holding the host lock competed with scheduler admission acquiring the host lock. Shutdown now closes admission under the lock and runs disposal outside it. A deterministic regression passes with the correction and fails within its bound against the old disposal mechanism. Shared early-failure completion likewise leaves the worker lock before releasing host admission. Two complete Hosting suites passed.

Callback cancellation stress found retained Python/TypeScript callback futures or closures when no host reply arrives, and a C# late-reply protocol failure. All three SDKs now release pending callback state before the terminal frame and retain only at most 4096 exact single-use callback identities. Each SDK passed 4,100 cancelled callbacks while preserving another active callback, accepting a recent late response and rejecting an evicted identity. Python/TypeScript's complete 17-test suite passed; C# regression passed twice.

Coverage now prints shared scenario boundaries, records completed suite evidence incrementally, and enforces a three-minute watchdog per owned suite process group. Timeout remains a failure, with no gate or assertion suppression. Three runner tests verify normal output, nonzero exit preservation and timeout failure.

## Static analysis of the merged implementation

The final local candidate for `abc38b0` passed with 3,526 reported assertions and 364 frozen files; PR #33 passed native macOS, Linux, Windows, coverage and CodeQL checks before merging as `0b630cf`. Main coverage completed all nine executable suites. SonarQube analyzed that exact main revision and reported 85.3% new-code coverage, 83.5% overall coverage, zero duplication and zero unreviewed hotspots, but correctly failed its zero-new-issues gate on 30 findings.

The follow-up resolves those findings in source: smaller catalog/worker/SDK methods; the existing SessionBinding groups shared configuration; internal cancellation tokens come last; intentionally independent tasks and noncooperative regression fixtures explicitly use CancellationToken.None; option errors identify actual parameters; JSON report options are cached. Gateway iterator cleanup asynchronously unregisters cancellation before returning transport ownership. Source size guards execute at the call boundary consistently for local and remote clients, with four regression checks before enumeration. No analyzer rule, quality gate or coverage condition was weakened, and public signatures remain unchanged.

Focused Hosting, three-language Shared, C# wire and three-language live-stream suites passed. Optional TLS/composition tests passed after rebuilding the local gateway fixture from current source; an initial run with a stale local fixture was refused by the protocol guard. Complete committed-candidate qualification and a fresh main SonarQube report remain necessary before publication.
