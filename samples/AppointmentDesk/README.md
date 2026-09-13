# Appointment Desk

A standalone package-consuming integration template for actions with uncertain responses. Two first-party scheduling strategies choose appointments; the application owns the calendar, approved commands and durable booking results. No NextPA integration, real calendar, network service or Docker engine is required.

## Run

From the repository root with the SDK selected by `global.json`:

```sh
./scripts/appointment-desk.sh --build
./scripts/appointment-desk.sh --list-strategies
./scripts/appointment-desk.sh --request late-demo --strategy latest
./scripts/appointment-desk.sh --request late-demo --strategy latest
```

The last two commands return the same booking ID. `earliest` v1 chooses the first available slot; `latest` v1 chooses the last. Both are installed implementations in one approved worker binary. Strategy identifiers cannot select arbitrary code. Each tenant/profile has an independent example calendar with six half-hour slots on **2030-01-14, 09:00–12:00 UTC**. Fixed future fixture dates are deliberate; no current-time scheduling is implied.

The default request ID is `demo-request`, so repeating the default command demonstrates replay. Use a new `--request` only for a genuinely new booking intention. The same scoped ID with a changed strategy, subject or time window is refused.

The build packs local WeavePort NuGet artifacts and restores/publishes this sample into its own artifact directory. Package restore may require internet access. Only the sample Contracts project is referenced directly; WeavePort libraries are package references.

## Demonstrate a lost response

```sh
./scripts/appointment-desk.sh --request recovery-demo --lose-response
# Expected exit code 2 and Status "uncertain": the actual worker process was killed after booking.
./scripts/appointment-desk.sh --request recovery-demo
```

The second command starts a new coordinator and worker, reads the retained command and returns its original booking ID. Inspect `artifacts/appointment-desk/calendar/calendar.json` between commands: the effect exists even when the first caller did not receive its result. The fault option terminates only the worker owned by that invocation. Omit it on the recovery command. Use a fresh store/request if the fixture calendar is already full.

## Options and results

| Option | Meaning |
|---|---|
| `--request ID` | Stable application request identity; maximum 128 characters |
| `--strategy earliest\|latest` | Approved selection implementation |
| `--tenant ID`, `--profile ID` | Trusted host context for a separate example calendar |
| `--subject TEXT` | Booking subject, maximum 128 characters |
| `--from ISO`, `--until ISO` | Containing window, explicit UTC offset, e.g. `2030-01-14T09:00:00Z` |
| `--store PATH` | Local store directory; defaults to `artifacts/appointment-desk/calendar` |
| `--lose-response` | Demonstrate worker termination after a committed callback |
| `--verify` | Run isolated executable verification |

`booked` includes the booking ID and selected slot. `conflict` is a retained terminal result for a slot taken after selection. `unavailable` means no slot was proposed and creates no intent. `uncertain` means dispatch did not produce a verified response; it is not evidence that the action failed. Repeat the exact request to reconcile it. A confirmed conflict requires a new request if the user wants another appointment; recovery never silently changes the selected slot.

Exit codes: 0 for a known domain result, 2 for uncertain dispatch, 130 for cancellation outside dispatch, 1 for invalid input/storage or pre-dispatch failure. Ctrl+C and a 60-second overall deadline cancel operations. These console context options are not an authentication system.

## Ownership and recovery

1. The strategy requests availability through `calendar.available` and proposes a slot.
2. The application validates the proposal and persists the exact command before execution.
3. The strategy executes through `calendar.book`, scoped to its host-bound tenant/profile and exact approved command.
4. The host checks the existing request outcome first. Otherwise it atomically decides conflict/booking within the local store mutation and records the outcome before replying.
5. The application validates the plugin's returned outcome against the stored result. An interrupted response retains the same intent for replay.

Same-key concurrent attempts converge on the first persisted command. Different requests competing for one scoped slot cannot both book it. Callback payloads do not supply tenant authority or arbitrary calendar paths. Missing grants and changed approved commands cause no callback effect. Native workers remain trusted same-user processes, without an OS sandbox.

The local store admits one coordinator through an exclusive owner handle and serializes in-process mutations. Commands and effects share one JSON snapshot, replaced from the same directory; failed writes do not publish a new in-memory booking. Reopening validates schema, scoped keys, slots and booking identities. Malformed or oversized data is refused without resetting the store. Limits are **1000 retained intents and 2 MiB**; history is never automatically evicted, and existing results can still be replayed at capacity.

This is a local transaction example. An external calendar requires an equivalent provider idempotency key or reconciliation mechanism; putting a local journal beside a non-idempotent remote API does not provide the same guarantee. Power-loss durability, distributed coordinators, network filesystems and hostile plugins are unqualified. No booking cancellation/rescheduling or retention migration is implemented. Do not rebuild trusted worker artifacts while operations are running.

## Verify and adapt

```sh
./scripts/appointment-desk.sh --build --verify
```

The suite covers strategy substitution, replay, changed input, availability, actual worker death after commit, coordinator reopen, competing bookings, overlapping customers, independent profiles, callback denial, changed command authority, cancellation before/after effect, storage write failure, ownership, corruption and capacity. See the [retained verification report (historical) — pre-public record](../../docs/history.md).

Adapt the sample-owned wish, slot and command contracts to the consuming application. Keep approval, scoped identity, conflict rules and effect reconciliation in the host/provider transaction boundary. The WeavePort runtime remains domain-independent; HiveWeaver, NextPA and TreeWeaver are unchanged.

## Shared installation identity

This sample now uses the packaged [installed-plugin resolver](../../docs/installed-plugins.md). Builds seal release directories; resolution validates content and contract identity. Activation changes future operations only. Do not modify or rebuild executing release/runtime files. The offline build now requires Python 3.11+ for manifest sealing.

Both release 1 and release 2 implement the same domain contract. Use `--activate 2` to change the default or `--version 1` for explicit selection. The selected installation is recorded with the result or persistent intent.

Calendar schema is now **2**. Older schema-1 stores are refused unchanged because their original installation identity was not recorded. Start this build with `--store artifacts/appointment-desk/calendar-v2`; use the original build to access an old store.

## Shared coordinator composition

The application now creates one shared coordinator at its composition root and injects it into every `Desk`. Concurrent requests therefore share worker and operation budgets. The reusable source, overload semantics, shutdown sequence and dependency ownership are documented in the [embedded coordinator guide](../../docs/embedded-coordinator.md). `--verify` also exercises actual concurrent workers, global capacity refusal, graceful drain, forced shutdown after commit and late callback accounting. See the [current coordinator evidence (historical) — pre-public record](../../docs/history.md).

## Guarded native restart

Normal CLI execution now creates `<store>/runtime/run.json` before workers start and uses a fresh workspace generation. Only confirmed clean shutdown clears the marker. An existing marker blocks execution and preserves booking state. Follow the [native recovery runbook](../../docs/native-operations.md) after an unclean stop; do not delete the marker merely to bypass the gate. Initial adoption from an older unguarded build also requires inspection of old processes. `./scripts/verify-recovery.sh` exercises an actual coordinator crash and supervised exact-request recovery in isolated test state.
