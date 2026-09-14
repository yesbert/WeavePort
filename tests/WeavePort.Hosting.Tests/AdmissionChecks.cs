using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeavePort.Hosting;

internal static class AdmissionChecks
{
    private static readonly string[] RejectionReasons = ["Admission rejected: concurrent-starts"];
    internal static async Task<int> RunAsync()
    {
        foreach (int limit in new[] { 8, 24 })
        {
            var logger = new Capture();
            await using var pool = new WorkerPool(new WorkerPoolOptions(MaximumConcurrentStarts: limit), TimeProvider.System, logger);
            var profile = new DelayedProfile();
            Task<Worker>[] starts = Enumerable.Range(0, limit).Select(i => pool.AcquireAsync(profile, "1", "private-tenant-" + i, default)).ToArray();
            try
            {
                Check(pool.Snapshot(0, 0, null).Starting == limit, "all configured slots occupied");
                try
                {
                    await pool.AcquireAsync(profile, "1", "private-overflow", default);
                    throw new Exception("Exceeded concurrent startup admission");
                }
                catch (WorkerCapacityException) { }
                Check(profile.Created == limit, "refused request never creates a worker");
                Check(logger.Reasons.SequenceEqual(RejectionReasons), "fixed safe rejection reason");
            }
            finally
            {
                profile.Ready.TrySetResult();
                await Task.WhenAll(starts);
            }
            await pool.AcquireAsync(profile, "1", "after-release", default);
            Check(profile.Created == limit + 1, "completed starts release permits");
        }
        return 8;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
    }

    private sealed record DelayedProfile() : ExecutionProfile(256, TimeSpan.FromSeconds(5), null)
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Created { get; private set; }
        public override ExecutionProtections Protection => ExecutionProtections.None;
        internal override Task<ExecutionProfile> ResolveAsync(CancellationToken token) => Task.FromResult<ExecutionProfile>(this);
        internal override ExecutionProfile Normalize() => this;
        internal override Worker CreateWorker(string version, TimeProvider clock)
        {
            Created++;
            return new DelayedWorker(this, version);
        }
    }

    private sealed class DelayedWorker(DelayedProfile profile, string version) : Worker(profile, version)
    {
        internal override Stream Input => Stream.Null;
        internal override bool Running => true;
        internal override Task StartAsync(CancellationToken token) => profile.Ready.Task.WaitAsync(token);
        internal override Task<JsonElement> DestroyAsync() => Task.FromResult(JsonSerializer.SerializeToElement(new { }));
    }

    private sealed class Capture : ILogger
    {
        internal List<string> Reasons { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (id.Id == 1006) Reasons.Add(formatter(state, error));
        }
    }
}
