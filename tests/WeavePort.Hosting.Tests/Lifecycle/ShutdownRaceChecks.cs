using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using WeavePort.Hosting;

internal static class ShutdownRaceChecks
{
    internal static async Task RunAsync()
    {
        var profile = new DelayedProfile();
        var pool = new WorkerPool(new WorkerPoolOptions(), TimeProvider.System, NullLogger.Instance);
        Task<Worker> start = pool.AcquireAsync(profile, "1", "a", default);
        Task disposal = pool.DisposeAsync().AsTask();
        await profile.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (disposal.IsCompleted || profile.Destroyed != 0)
        {
            throw new Exception("Shutdown released an in-progress startup");
        }

        if (!ReferenceEquals(disposal, pool.DisposeAsync().AsTask()))
        {
            throw new Exception("Concurrent shutdown did not share completion");
        }

        profile.Ready.SetResult();
        try
        {
            await start;
            throw new Exception("Worker was published after shutdown");
        }
        catch (OperationCanceledException) { }
        await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        if (profile.Destroyed != 1 || pool.Snapshot(0, 0, null).Workers != 0)
        {
            throw new Exception("Startup cleanup was not confirmed exactly once");
        }

        try
        {
            await pool.AcquireAsync(profile, "1", "b", default);
            throw new Exception("Shutdown accepted a new worker");
        }
        catch (ObjectDisposedException) { }
        Console.WriteLine("PASS shutdown drains pending startup and rejects late admission");
    }

    private sealed record DelayedProfile() : ExecutionProfile(256, null, null)
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Started { get; set; }
        internal int Destroyed { get; set; }
        public override ExecutionProtections Protection => ExecutionProtections.None;
        internal override ExecutionProfile Normalize() => this;
        internal override Task<ExecutionProfile> ResolveAsync(CancellationToken token) => Task.FromResult<ExecutionProfile>(this);
        internal override Worker CreateWorker(string version, TimeProvider clock) => new DelayedWorker(this, version);
    }

    private sealed class DelayedWorker(DelayedProfile profile, string version) : Worker(profile, version)
    {
        internal override Stream Input => Stream.Null;
        internal override bool Running => true;
        internal override async Task StartAsync(CancellationToken token)
        {
            using var registration = token.Register(() => profile.Cancelled.TrySetResult());
            await profile.Ready.Task;
            profile.Started = true;
        }
        internal override Task<JsonElement> DestroyAsync()
        {
            if (!profile.Started)
            {
                throw new Exception("Cleanup raced startup");
            }

            profile.Destroyed++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
            }));
        }
    }
}
