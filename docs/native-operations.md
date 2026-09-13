# Native deployment and recovery runbook

This runbook qualifies a **guarded, manually supervised local macOS reference deployment** of Appointment Desk. Automatic recovery of arbitrary native descendants and power-loss durability are not qualified. The [run guard](../samples/Shared/NativeRunGuard.cs) is application-owned source; the core runtime has no persistent orphan registry. Other applications must adopt an equivalent deployment gate before inheriting this procedure.

## Deployment acceptance

| Boundary | Required decision / verified behavior |
|---|---|
| Code and runtime | Owner-controlled cooperative plugins, approved installation manifests and stable runtime/worker bytes; no daemonized descendants in the qualified fixture |
| Coordinator ownership | One intentional coordinator per budget; one guarded store/runtime root per deployment instance. Different roots still create independent budgets |
| Filesystem | Trusted private local directory, enough free space for state and workspaces; no concurrent operator mutation, network-filesystem or power-loss guarantee |
| Supervision | Before enabling automatic restart, independently qualify ownership and termination of the complete process boundary. This slice installs no service supervisor and leaves automatic restart gated |
| Existing installations | Stop and inspect old unguarded processes/workspaces before first adoption. A missing new marker does not prove an old deployment was clean |
| State and versions | Preserve exact request identity and pinned installation; never rebuild, patch or remove files needed by live or recoverable operations |

Use [the coordinator template](embedded-coordinator.md) for admission limits and shutdown wiring. The reference CLI owns one coordinator for its run; it is not a continuously serving daemon. Native reservations are estimates and do not enforce hard memory, CPU or filesystem ceilings. An operator must supply capacity monitoring and a qualified service boundary before production rollout.

## Normal start and stop

Normal Appointment Desk commands acquire the local calendar owner and create `<store>/runtime/run.json` exclusively before creating any worker. The marker records schema 1 and a random generation, with no credentials or process-kill authority. Worker workspace roots use `<store>/runtime/workers/<generation>`. Empty, malformed or existing markers block execution without replacing their contents.

A normal completed invocation drains the shared coordinator. Only a clean snapshot permits removal of an empty generation directory and the marker. Unexpected remaining generation files keep the marker in place. Disposing a file handle or merely observing the coordinator process exit does not mark a run clean. Failure to remove the marker also keeps restart blocked.

For an embedded service: stop accepting requests, call `StopAsync`, inspect the returned snapshot and keep callback dependencies alive while outstanding work remains. Use the existing request identity to reconcile uncertain outcomes. The CLI uses five seconds grace plus five seconds observation after its operation, and exits with an explicit diagnostic when cleanup is incomplete; its retained marker blocks the next execution. Ctrl+C and the existing 60-second outer timeout cancel CLI work. Forced termination and supervisor deadlines must leave time for normal drain; no fixed deadline guarantees arbitrary managed callbacks stopped.

## After an unclean stop

1. **Keep ingress and automatic restart disabled.** A `Native startup blocked` diagnostic is an intentional gate. Repeated launches, a different store path or deleting `run.json` are not recovery mechanisms.
2. **Establish termination of the complete old deployment.** Use independently qualified supervisor ownership, not saved PIDs, `Instance` suffixes, process names, EOF or elapsed time. If termination cannot be established, keep restart blocked. Do not use broad process-name kills against a shared desktop account. A full machine restart ends old processes, but does not establish that persisted application state is valid or power-loss durable.
3. **Preserve evidence before changing files.** Retain `runtime/run.json`, its referenced generation directory, the original `calendar.json`, relevant diagnostics and the exact pinned installation/runtime artifacts. Keep access restricted: workspace/state files may contain application data. Preserve an untouched copy outside the active root; do not edit booking IDs, requests, installation pins or schema values.
4. **Archive the old run under operator control.** Only after step 2, move the old generation directory and `run.json` to a private quarantine directory. Do not merge the generation into a new workspace or recursively delete unrelated directories. Marker content is diagnostic input, not a trusted arbitrary filesystem path. For schema 1 the generation is a 32-character hexadecimal identifier under that deployment's known `runtime/workers` directory. A partial/invalid marker requires inspection of the entire known deployment root.
5. **Reopen using the original installation and exact request.** The application validates the retained calendar and installation pin. On malformed state or unavailable/changed pinned artifacts, stop and restore the matching approved deployment/validated backup through the application's recovery process. Do not reset history or silently choose the active default version.
6. **Inspect the domain result and cleanup.** A previously committed booking returns its original identity. An intent without a terminal outcome can execute only when explicitly resubmitted under the same key. The application does not scan and replay pending intents automatically. Confirm a clean run removes its marker; re-enable ingress only after deployment checks pass.

The marker records potential unclean execution, not a durable transaction or a complete list of worker processes. The guard flushes file buffers before launch; it does not synchronize calendar state and directory entries into a power-loss transaction. Filesystem corruption, full disks, OS crashes and restoration from backups require separate qualification. Windows native qualification remains open.

## Diagnostics and retention

| Evidence | Interpretation and retention |
|---|---|
| `CoordinatorSnapshot.Active`, `ShutdownFinished`, `Clean` | An observation deadline is not work completion. Inspect `Clean` even after the lifecycle task finishes |
| Runtime workers, bindings, tenants, quarantine and maintenance failure | Outstanding callbacks and unconfirmed cleanup remain visible within a live coordinator; those counters do not survive root death |
| `runtime/run.json` and generation directories | Retain after unclean stop until supervised investigation/archive. Clean runs remove only their empty generation root; no reuse of abandoned files |
| Calendar command/outcome and installation pin | Keep for the application's recovery/idempotency lifetime. The sample has 1000 intents / 2 MiB capacity and no automatic eviction or migration |
| Installed releases and runtime bytes | Keep every exact installation needed by retained state; activation does not authorize deletion of old releases |
| Quarantine, reports and logs | Apply the owner's retention/access policy after investigation. No automatic quarantine deletion is implemented. Avoid payloads, secrets and sensitive calendar content in routine logs |

Do not infer a booking failed from exit code 1, cancellation or a cleanup diagnostic. Check the retained command/outcome under the original key. A run that returned no response may already have committed its effect. Real external providers need their own idempotency or reconciliation contract.

## Reproduce the crash scenario

```sh
./scripts/appointment-desk.sh --build --verify
./scripts/verify-recovery.sh
./scripts/verify-compatibility.sh
```

The crash verifier requires macOS and launches only isolated test state. It creates a new POSIX session, waits for a post-booking barrier, sends SIGKILL to the coordinator root, confirms startup refusal, then terminates its own test process group and archives the run evidence before explicit recovery. Its process-group authority comes from that launch, not from a stored PID. The cooperative fixture does not daemonize; this test cannot prove containment of arbitrary native children.

`--hold-after-booking PATH` is a test-only Appointment Desk fault option that creates a new barrier file after commit and waits until cancellation. Use it only with isolated stores and the crash driver; it is not a plugin operation or recovery command. The driver records whether the fixture worker was still running immediately after the crash and preserves leftover workspace evidence. See the [retained report (historical) — pre-public record](history.md).
