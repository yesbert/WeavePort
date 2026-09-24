using System.Text.Json;
using AppointmentDesk.Contracts;
using WeavePort.Samples;

namespace AppointmentDesk.Host;

internal static class GuardedBooking
{
    internal static async Task<Outcome> RunAsync(RuntimePaths runtime, string storePath, Request request, CancellationToken token, bool loseResponse, string? holdAfterBooking)
    {
        await using var store = new CalendarStore(storePath);
        using var guard = new NativeRunGuard(Path.Combine(storePath, "runtime"));
        var coordinator = new EmbeddedCoordinator();
        runtime = runtime with
        {
            WorkerRoot = guard.WorkspaceRoot
        };
        try
        {
            Hooks? hooks = loseResponse || holdAfterBooking is not null ? new(Bound: (session, callbacks) =>
            {
                callbacks.AfterBooking = async () =>
                {
                    if (holdAfterBooking is not null)
                    {
                        using (var marker = new FileStream(holdAfterBooking, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        {
                            JsonSerializer.Serialize(marker, new
                            {
                                session.Instance,
                                guard.Generation
                            });
                            marker.Flush(flushToDisk: true);
                        }

                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }

                    if (loseResponse)
                    {
                        await WorkerFaults.KillAsync(session, token);
                    }
                };
                return Task.CompletedTask;
            }) : null;
            return await new Desk(runtime, store, coordinator).RunAsync(request, token, hooks);
        }
        finally
        {
            var stopped = await coordinator.StopAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            if (!stopped.Clean)
            {
                throw new InvalidOperationException("Native shutdown incomplete. Preserve runtime/run.json and follow docs/native-operations.md.");
            }

            guard.MarkClean(stopped);
        }
    }
}
