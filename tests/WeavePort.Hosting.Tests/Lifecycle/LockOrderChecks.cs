using System.Reflection;
using WeavePort.Hosting;

internal static class LockOrderChecks
{
    internal static async Task RunAsync()
    {
        var host = new PluginHost(new SchedulingOptions { MaximumPristineWorkers = 0 });
        object hostSync = Field(host, "_sync");
        object scheduler = Field(host, "_scheduler");
        object schedulerSync = Field(scheduler, "_sync");
        // Make the async disposal prefix complete synchronously up to scheduler disposal.
        await DisposalChecks.Source(host).CancelAsync();
        await ((Task)Field(host, "_maintenance")).WaitAsync(TimeSpan.FromSeconds(3));
        using var schedulerHeld = new ManualResetEventSlim();
        using var inspectHost = new ManualResetEventSlim();
        var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedulerThread = new Thread(() =>
        {
            lock (schedulerSync)
            {
                schedulerHeld.Set();
                inspectHost.Wait();
                bool entered = Monitor.TryEnter(hostSync, TimeSpan.FromSeconds(2));
                if (entered)
                {
                    Monitor.Exit(hostSync);
                }

                observed.TrySetResult(entered);
            }
        })
        {
            IsBackground = true
        };
        schedulerThread.Start();
        if (!schedulerHeld.Wait(TimeSpan.FromSeconds(3)))
        {
            throw new Exception("Scheduler lock fixture failed.");
        }

        Task shutdown = Task.Run(async () => await host.DisposeAsync());
        // With the scheduler held, its disposal cannot complete; the host lock must remain available.
        if (!SpinWait.SpinUntil(() => (bool)Field(host, "_disposed"), TimeSpan.FromSeconds(3)))
        {
            inspectHost.Set();
            throw new Exception("Host shutdown did not close admission.");
        }
        inspectHost.Set();
        bool hostAvailable = await observed.Task.WaitAsync(TimeSpan.FromSeconds(4));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(4));
        if (!hostAvailable)
        {
            throw new Exception("Host shutdown held the host lock while waiting for the scheduler lock.");
        }

        Console.WriteLine("PASS host shutdown does not invert scheduler-to-host lock order");
    }

    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
}
