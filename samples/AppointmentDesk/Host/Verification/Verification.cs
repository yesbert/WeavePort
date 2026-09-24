using System.Text.Json;
using AppointmentDesk.Contracts;
using WeavePort.Abstractions;
using WeavePort.Samples;

namespace AppointmentDesk.Host;

internal static class Verification
{
    private static int _checks;
    internal static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("FAIL: " + message);
        }

        _checks++;
        Console.WriteLine("PASS: " + message);
    }

    internal static Request Request(string id, string strategy = "earliest", string tenant = "a") => new(new Scope(tenant, "default"), id, strategy, Model.DefaultWish);
    internal static async Task RefusedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("Expected refusal.");
    }

    internal static async Task RunAsync(RuntimePaths runtime, EmbeddedCoordinator coordinator, CancellationToken token)
    {
        string root = Path.Combine(runtime.Root, "verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        runtime = runtime with
        {
            SelectedVersion = null,
            Selector = Path.Combine(root, "active-version.txt")
        };
        runtime.Activate("1");
        await using (var store = new CalendarStore(Path.Combine(root, "selection")))
        {
            var desk = new Desk(runtime, store, coordinator);
            var first = await desk.RunAsync(Request("first"), token);
            var last = await desk.RunAsync(Request("last", "latest"), token);
            Check(first.Status == "booked" && first.Slot == Model.Slots[0] && last.Slot == Model.Slots[^1], "installed strategies select different ends of availability");
            Check(await desk.RunAsync(Request("first"), token) == first, "identical retry returns original booking");
            await RefusedAsync(() => desk.RunAsync(Request("first", "latest"), token));
            await RefusedAsync(() => desk.RunAsync(Request("first") with { Wish = Model.DefaultWish with { Subject = "Changed" } }, token));
            Check(true, "same scoped request refuses changed strategy or wish");
            var none = Request("none") with
            {
                Wish = Model.DefaultWish with
                {
                    From = Model.Slots[^1].End,
                    Until = Model.Slots[^1].End.AddHours(1)
                }
            };
            Check((await desk.RunAsync(none, token)).Status == "unavailable" && await store.FindAsync(none, token) is null, "unavailable wish creates no intent or booking");
            await RefusedAsync(() => desk.RunAsync(Request("invalid", "unknown"), token));
            Check(true, "unknown strategy fails before dispatch");
        }

        await VerifyActivationAsync(runtime, root, coordinator, token);
        await VerifyLostResponseAsync(runtime, root, coordinator, token);
        await VerifyConflictsAsync(runtime, root, coordinator, token);
        await VerifyInterruptedDispatchAsync(runtime, root, coordinator, token);
        await StorageVerification.RunAsync(root, token);
        await CoordinatorVerification.RunAsync(runtime, root, token);
        await RunGuardVerification.RunAsync(root);
        await File.WriteAllTextAsync(Path.Combine(root, "result.txt"), $"{_checks} assertions passed.\n", token);
        Console.WriteLine($"Verification passed: {_checks} assertions. Evidence: {root}");
    }

    private static async Task VerifyLostResponseAsync(RuntimePaths runtime, string root, EmbeddedCoordinator coordinator, CancellationToken token)
    {
        string path = Path.Combine(root, "recovery");
        Outcome? committed;
        var request = Request("lost-response");
        await using (var store = new CalendarStore(path))
        {
            var desk = new Desk(runtime, store, coordinator);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = desk.RunAsync(request, token, new(Bound: (session, callbacks) =>
            {
                callbacks.AfterBooking = async () =>
                {
                    reached.TrySetResult();
                    await release.Task.WaitAsync(token);
                    await WorkerFaults.KillAsync(session, token);
                };
                return Task.CompletedTask;
            }));
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                committed = (await store.FindAsync(request, token))?.Outcome;
                var other = await desk.RunAsync(Request("lost-response", tenant: "b"), token);
                Check(!failed.IsCompleted && other.Status == "booked" && other.Slot == Model.Slots[0] && other.BookingId != committed?.BookingId, "customer B completes independently while A awaits post-commit response");
            }
            finally
            {
                release.TrySetResult();
            }

            Check((await failed).Status == "uncertain" && committed?.Status == "booked", "actual worker death after commit reports uncertainty, not definite failure");
        }

        await using (var reopened = new CalendarStore(path))
        {
            var result = await new Desk(runtime, reopened, coordinator).RunAsync(request, token);
            Check(result == committed, "reopened store and fresh worker return original booking identity");
            Check((await reopened.AvailableAsync(request, token)).Length == 5, "lost-response recovery creates exactly one booking in customer scope");
        }
    }

    private static async Task VerifyInterruptedDispatchAsync(RuntimePaths runtime, string root, EmbeddedCoordinator coordinator, CancellationToken token)
    {
        await using var store = new CalendarStore(Path.Combine(root, "interruptions"));
        var desk = new Desk(runtime, store, coordinator);
        var before = Request("before-effect");
        IPluginSession? owned = null;
        var failed = await desk.RunAsync(before, token, new(Bound: (session, _) =>
        {
            owned = session;
            return Task.CompletedTask;
        }, Prepared: () => WorkerFaults.KillAsync(owned!, token)));
        Check(failed.Status == "booked" && (await store.FindAsync(before, token))?.Outcome == failed, "worker lost between calls is replaced and executes the retained intent");
        Check(await desk.RunAsync(before, token) == failed, "retry after pre-dispatch replacement returns the same booking");
        var after = Request("cancel-after-effect");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var interrupted = await desk.RunAsync(after, cancel.Token, new(Bound: (_, callbacks) =>
        {
            callbacks.AfterBooking = () =>
            {
                cancel.Cancel();
                return Task.CompletedTask;
            };
            return Task.CompletedTask;
        }));
        var recorded = (await store.FindAsync(after, token))?.Outcome;
        Check(interrupted.Status == "uncertain" && recorded?.Status == "booked", "cancellation after commit preserves the real effect");
        Check(await desk.RunAsync(after, token) == recorded, "retry reconciles post-commit cancellation without duplication");
        var profile = before with
        {
            Scope = before.Scope with
            {
                Profile = "other"
            }
        };
        var independent = await desk.RunAsync(profile, token);
        Check(independent.Status == "booked" && independent.Slot == Model.Slots[0], "profiles of one tenant own independent calendars and request keys");
    }

    private static async Task VerifyConflictsAsync(RuntimePaths runtime, string root, EmbeddedCoordinator coordinator, CancellationToken token)
    {
        await using var store = new CalendarStore(Path.Combine(root, "conflicts"));
        var desk = new Desk(runtime, store, coordinator);
        var a = Request("race-a");
        var b = Request("race-b");
        await store.PrepareAsync(a, Model.Slots[0], token, runtime.Resolve().Identity);
        await store.PrepareAsync(b, Model.Slots[0], token, runtime.Resolve().Identity);
        var results = await Task.WhenAll(desk.RunAsync(a, token), desk.RunAsync(b, token));
        Check(results.Count(r => r.Status == "booked") == 1 && results.Count(r => r.Status == "conflict") == 1, "concurrent approved intents cannot double-book a scoped slot");
        Check(await desk.RunAsync(a, token) == results[0] && await desk.RunAsync(b, token) == results[1], "terminal success and conflict remain stable on retry");
        var denied = Request("denied");
        BookingCallbacks? callbacks = null;
        var result = await desk.RunAsync(denied, token, new(Bound: (_, c) =>
        {
            callbacks = c;
            return Task.CompletedTask;
        }, DenyBook: true));
        Check(result.Status == "uncertain" && callbacks?.BookCalls == 0 && (await store.FindAsync(denied, token))?.Outcome is null, "missing booking grant causes no effect and retains retryable intent");
        Check((await desk.RunAsync(denied, token)).Status == "booked", "authorized retry can complete retained intent");
        var cancelRequest = Request("cancelled");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelled = await desk.RunAsync(cancelRequest, cancel.Token, new(Prepared: () =>
        {
            cancel.Cancel();
            return Task.CompletedTask;
        }));
        Check(cancelled.Status == "uncertain" && (await store.FindAsync(cancelRequest, token))?.Outcome is null, "cancelled dispatch retains exact intent without claiming failure");
        Check((await desk.RunAsync(cancelRequest, token)).Status == "booked", "retry after cancellation completes the same intent");
        await VerifyAuthorityAsync(runtime, store, token);
    }

    private static async Task VerifyAuthorityAsync(RuntimePaths runtime, CalendarStore store, CancellationToken token)
    {
        var request = Request("authority");
        var entry = await store.PrepareAsync(request, Model.Slots[^1], token, runtime.Resolve().Identity);
        var callbacks = new BookingCallbacks(store, request)
        {
            Approved = entry.Command
        };
        var context = new PluginContext("a", "earliest", "1", "default", JsonSerializer.SerializeToElement(new
        {
        }));
        HostCall Call(PluginContext c, Command command) => new(c, "test", "calendar.book", JsonSerializer.SerializeToElement(command), "test");
        await RefusedAsync(async () => await callbacks.InvokeAsync(Call(context with { Tenant = "b" }, entry.Command), token));
        await RefusedAsync(async () => await callbacks.InvokeAsync(Call(context with { Profile = "other" }, entry.Command), token));
        await RefusedAsync(async () => await callbacks.InvokeAsync(Call(context, entry.Command with { Slot = Model.Slots[1] }), token));
        await RefusedAsync(async () => await callbacks.InvokeAsync(Call(context, entry.Command with { RequestId = "other" }), token));
        Check(callbacks.BookCalls == 0 && (await store.FindAsync(request, token))?.Outcome is null, "foreign scope and altered approved commands create no booking");
    }

    private static async Task VerifyActivationAsync(RuntimePaths runtime, string root, EmbeddedCoordinator coordinator, CancellationToken token)
    {
        string path = Path.Combine(root, "activation");
        var request = Request("pinned");
        Outcome? original = null;
        await using (var store = new CalendarStore(path))
        {
            var desk = new Desk(runtime, store, coordinator);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool killed = false;
            var pending = desk.RunAsync(request, token, new(Bound: (session, callbacks) =>
            {
                callbacks.AfterBooking = async () =>
                {
                    reached.TrySetResult();
                    await release.Task.WaitAsync(token);
                    await WorkerFaults.KillAsync(session, token);
                    killed = true;
                };
                return Task.CompletedTask;
            }));
            try
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                runtime.Activate("2");
                var other = Request("new-release", tenant: "b");
                Check((await desk.RunAsync(other, token)).Status == "booked" && !pending.IsCompleted && (await store.FindAsync(other, token))?.Installation.Version == "2", "new booking selects v2 while v1 worker remains live");
            }
            finally
            {
                release.TrySetResult();
            }

            var interrupted = await pending;
            Check(killed, "activation recovery test actually terminated the owned v1 worker");
            original = (await store.FindAsync(request, token))?.Outcome;
            Check(interrupted.Status == "uncertain" && (await store.FindAsync(request, token))?.Installation.Version == "1", "post-commit worker loss retains v1 installation across activation");
        }

        await using (var store = new CalendarStore(path))
        {
            Check(await new Desk(runtime, store, coordinator).RunAsync(request, token) == original, "reopened booking replays exact v1 installation under v2 default");
            await RefusedAsync(() => new Desk(runtime with { SelectedVersion = "2" }, store, coordinator).RunAsync(request, token));
            Check((await store.FindAsync(request, token))?.Outcome == original, "conflicting explicit release preserves original booking");
        }

        runtime.Activate("1");
    }
}
