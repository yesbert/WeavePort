using AppointmentDesk.Contracts;
using WeavePort.Abstractions;
using WeavePort.Hosting;
using WeavePort.Samples;

namespace AppointmentDesk.Host;

internal static class CoordinatorVerification
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool condition, string message) => Verification.Check(condition, message);
    private static WorkerPoolOptions Budget(int count) => new(MaximumWorkers: count, MemoryBudgetMiB: count * 256, MaximumPristineWorkers: 0, MaximumConcurrentStarts: count, MaximumWorkersPerTenant: count);
    internal static async Task RunAsync(RuntimePaths runtime, string root, CancellationToken token)
    {
        await SharedBudgetAsync(runtime, root, token);
        await RuntimeBudgetAsync(runtime, root, token);
        await GracefulAsync(runtime, root, token);
        await ForcedAsync(runtime, root, token);
        await LateWorkAsync(token);
        await AdmissionAsync(token);
        await CancellationFailureAsync(token);
        await CleanupFailureAsync();
    }

    private static async Task RejectedAsync(Func<Task> action, string expected)
    {
        try
        {
            await action();
        }
        catch (CoordinatorRejectedException error) when (error.Status == expected)
        {
            return;
        }

        throw new InvalidOperationException("Expected coordinator rejection: " + expected);
    }

    private static async Task SharedBudgetAsync(RuntimePaths runtime, string root, CancellationToken token)
    {
        await using var store = new CalendarStore(Path.Combine(root, "shared-budget"));
        await using var coordinator = new EmbeddedCoordinator(2, Budget(2));
        var desk = new Desk(runtime, store, coordinator);
        var reachedA = Signal();
        var reachedB = Signal();
        var releaseA = Signal();
        var releaseB = Signal();
        IPluginSession? aSession = null, bSession = null;
        var a = desk.RunAsync(Verification.Request("shared", tenant: "a"), token, new(Bound: (session, callbacks) =>
        {
            aSession = session;
            callbacks.AfterBooking = async () =>
            {
                reachedA.TrySetResult();
                await releaseA.Task.WaitAsync(token);
                await WorkerFaults.KillAsync(session, token);
            };
            return Task.CompletedTask;
        }));
        Task<Outcome>? b = null;
        try
        {
            await reachedA.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            b = desk.RunAsync(Verification.Request("shared", tenant: "b"), token, new(Bound: (session, callbacks) =>
            {
                bSession = session;
                callbacks.AfterBooking = async () =>
                {
                    reachedB.TrySetResult();
                    await releaseB.Task.WaitAsync(token);
                };
                return Task.CompletedTask;
            }));
            await reachedB.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var snapshot = coordinator.Snapshot;
            Check(snapshot.Active == 2 && snapshot.Runtime.Workers == 2 && snapshot.Runtime.Bindings == 2 && snapshot.Runtime.ReservedMemoryMiB == 512 && aSession!.Instance != bSession!.Instance, "two tenant workers share one counted budget with distinct instances");
            var extra = Verification.Request("overload", tenant: "c");
            await RejectedAsync(() => desk.RunAsync(extra, token), "busy");
            Check(await store.FindAsync(extra, token) is null && coordinator.Snapshot.Active == 2, "overload refuses immediately without intent or extra operation");
            releaseB.TrySetResult();
            Check((await b).Status == "booked" && !a.IsCompleted, "B completes within shared host while A holds its callback");
        }
        finally
        {
            releaseA.TrySetResult();
            releaseB.TrySetResult();
        }

        Check((await a).Status == "uncertain", "A worker loss within shared coordinator remains uncertain");
        if (b is not null)
        {
            await b;
        }

        var recovered = await desk.RunAsync(Verification.Request("shared", tenant: "a"), token);
        Check(recovered.Status == "booked" && (await store.AvailableAsync(Verification.Request("shared", tenant: "a"), token)).Length == 5, "shared coordinator reuses released capacity and reconciles one original booking");
        Check(coordinator.Snapshot.Active == 0 && coordinator.Snapshot.Runtime.Workers == 0 && coordinator.Snapshot.Runtime.Bindings == 0, "operation completion includes session and worker disposal");
    }

    private static async Task RuntimeBudgetAsync(RuntimePaths runtime, string root, CancellationToken token)
    {
        await using var store = new CalendarStore(Path.Combine(root, "runtime-budget"));
        await using var coordinator = new EmbeddedCoordinator(3, Budget(1));
        var desk = new Desk(runtime, store, coordinator);
        var reached = Signal();
        var release = Signal();
        var a = desk.RunAsync(Verification.Request("hold"), token, new(Bound: (_, callbacks) =>
        {
            callbacks.AfterBooking = async () =>
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(token);
            };
            return Task.CompletedTask;
        }));
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var request = Verification.Request("worker-overload", tenant: "b");
            try
            {
                await desk.RunAsync(request, token);
                throw new InvalidOperationException("Expected runtime busy.");
            }
            catch (WeavePort.Sdk.Client.PluginCallException error) when (error.Status == "busy" && !error.MayHaveExecuted)
            {
            }

            Check(await store.FindAsync(request, token) is null && coordinator.Snapshot.Runtime.Workers == 1 && coordinator.Snapshot.Failed == 1, "shared runtime quota rejects before proposal even when operation capacity remains");
        }
        finally
        {
            release.TrySetResult();
        }

        await a;
    }

    private static async Task GracefulAsync(RuntimePaths runtime, string root, CancellationToken token)
    {
        await using var store = new CalendarStore(Path.Combine(root, "graceful"));
        await using var coordinator = new EmbeddedCoordinator(2, Budget(2));
        var reached = Signal();
        var release = Signal();
        var desk = new Desk(runtime, store, coordinator);
        var operation = desk.RunAsync(Verification.Request("drain"), token, new(Bound: (_, callbacks) =>
        {
            callbacks.AfterBooking = async () =>
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(token);
            };
            return Task.CompletedTask;
        }));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        var stopping = coordinator.StopAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        try
        {
            await RejectedAsync(() => desk.RunAsync(Verification.Request("late"), token), "stopping");
            Check(!stopping.IsCompleted && coordinator.Snapshot.Active == 1, "drain closes admission while accepted work stays active");
        }
        finally
        {
            release.TrySetResult();
        }

        Check((await operation).Status == "booked", "grace interval permits accepted booking to return normally");
        var stopped = await stopping;
        Check(stopped.Clean && !stopped.CancellationRequested, "graceful shutdown confirms operation and runtime cleanup without cancellation");
        Check((await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.Zero)).Clean, "repeated shutdown observes completed lifecycle");
    }

    private static async Task ForcedAsync(RuntimePaths runtime, string root, CancellationToken token)
    {
        await using var store = new CalendarStore(Path.Combine(root, "forced"));
        var coordinator = new EmbeddedCoordinator(1, Budget(1));
        var reached = Signal();
        var release = Signal();
        var request = Verification.Request("forced");
        var operation = new Desk(runtime, store, coordinator).RunAsync(request, token, new(Bound: (_, callbacks) =>
        {
            // Deliberately ignores the invocation token, modelling external work after cancellation.
            callbacks.AfterBooking = async () =>
            {
                reached.TrySetResult();
                await release.Task;
            };
            return Task.CompletedTask;
        }));
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var result = await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.FromSeconds(3));
            Check(result.CancellationRequested && !result.Clean && result.Runtime.Tenants == 1, "forced shutdown reports outstanding detached callback rather than false clean stop");
            Check((await operation.WaitAsync(TimeSpan.FromSeconds(5), token)).Status == "uncertain", "shutdown after committed effect returns uncertainty");
        }
        finally
        {
            release.TrySetResult();
        }

        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5), token);
        await WaitCleanAsync(coordinator, token);
        await coordinator.DisposeAsync();
        await using var replacement = new EmbeddedCoordinator(1, Budget(1));
        var recovered = await new Desk(runtime, store, replacement).RunAsync(request, token);
        Check(recovered == (await store.FindAsync(request, token))?.Outcome && recovered.Status == "booked" && (await store.AvailableAsync(request, token)).Length == 5, "new coordinator reconciles forced-stop booking without duplication");
    }

    private static async Task WaitCleanAsync(EmbeddedCoordinator coordinator, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        while (!coordinator.Snapshot.Clean)
        {
            await Task.Delay(10, deadline.Token);
        }

        Check(true, "released late callback eventually removes retained tenant accounting");
    }

    private static async Task LateWorkAsync(CancellationToken token)
    {
        var coordinator = new EmbeddedCoordinator(1);
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = coordinator.RunAsync((_, _) => release.Task, token);
        var result = await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
        Check(!result.Clean && !result.ShutdownFinished && result.Active == 1 && result.CancellationRequested, "bounded shutdown retains uncooperative operation accounting and pending completion");
        await RejectedAsync(() => coordinator.RunAsync((_, _) => Task.FromResult(2)), "stopping");
        release.TrySetResult(1);
        Check(await operation == 1, "late operation result is preserved after observation deadline");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5), token);
        Check(coordinator.Snapshot.Clean, "eventual completion is observable after late work finishes");
        await coordinator.DisposeAsync();
    }

    private static async Task AdmissionAsync(CancellationToken token)
    {
        await using var coordinator = new EmbeddedCoordinator(2);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool invoked = false;
        try
        {
            await coordinator.RunAsync((_, _) =>
            {
                invoked = true;
                return Task.FromResult(0);
            }, cancelled.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Check(!invoked && coordinator.Snapshot.Active == 0, "pre-cancelled operation never runs its delegate");
        try
        {
            await coordinator.RunAsync<int>((_, _) => throw new IOException("Scoped failure"));
        }
        catch (IOException)
        {
        }

        Check(await coordinator.RunAsync((_, _) => Task.FromResult(7)) == 7 && coordinator.Snapshot.Failed == 1, "failed delegate releases its permit and preserves failure diagnostics");
        var release = Signal();
        int accepted = 0, refused = 0;
        var attempts = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            try
            {
                await coordinator.RunAsync(async (_, _) =>
                {
                    Interlocked.Increment(ref accepted);
                    await release.Task.WaitAsync(token);
                    return 1;
                }, token);
            }
            catch (CoordinatorRejectedException error) when (error.Status == "busy")
            {
                Interlocked.Increment(ref refused);
            }
        }, token)).ToArray();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref accepted) + Volatile.Read(ref refused) != 24)
            {
                await Task.Delay(10, deadline.Token);
            }

            Check(accepted == 2 && refused == 22 && coordinator.Snapshot.Active == 2, "concurrent admission cannot exceed the configured operation limit");
        }
        finally
        {
            release.TrySetResult();
        }

        await Task.WhenAll(attempts);
    }

    private static async Task CancellationFailureAsync(CancellationToken token)
    {
        var coordinator = new EmbeddedCoordinator();
        var release = Signal();
        var operation = coordinator.RunAsync(async (_, admittedToken) =>
        {
            using var registration = admittedToken.Register(() => throw new IOException("Cancellation callback failure"));
            await release.Task;
            return 1;
        }, token);
        try
        {
            var result = await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            Check(!result.Clean && result.Active == 1 && result.Runtime.Workers == 0, "cancellation callback failure does not skip host cleanup or release active work");
        }
        finally
        {
            release.TrySetResult();
        }

        await operation;
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5), token);
        Check(coordinator.Snapshot.CleanupErrors.SequenceEqual([nameof(AggregateException)]) && !coordinator.Snapshot.Clean, "cancellation callback exception is retained after eventual completion");
    }

    private static async Task CleanupFailureAsync()
    {
        int disposals = 0;
        var coordinator = new EmbeddedCoordinator(disposeHost: async host =>
        {
            disposals++;
            await host.DisposeAsync();
            throw new IOException("Simulated unconfirmed application cleanup.");
        });
        var result = await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Check(!result.Clean && result.ShutdownFinished && result.CleanupErrors.SequenceEqual([nameof(IOException)]), "cleanup failure remains explicit after shutdown lifecycle finishes");
        await coordinator.StopAsync(TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Check(disposals == 1 && coordinator.Snapshot.CleanupErrors.Length == 1, "repeated stop does not repeat disposal or erase errors");
        try
        {
            await coordinator.DisposeAsync();
            throw new IOException("Expected incomplete disposal.");
        }
        catch (InvalidOperationException)
        {
            Check(true, "dispose refuses to silently acknowledge failed cleanup");
        }
    }
}
