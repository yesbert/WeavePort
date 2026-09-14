using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WeavePort.Hosting;

internal static class QuarantineChecks
{
    internal static async Task<int> RunAsync()
    {
        var clock = new ManualClock();
        await using var pool = new WorkerPool(new WorkerPoolOptions(), clock, NullLogger.Instance);
        var profile = new ControlledProfile();
        var first = (ControlledWorker)await pool.AcquireAsync(profile, "1", "a", default);
        var second = (ControlledWorker)await pool.AcquireAsync(profile, "1", "b", default);
        Check(pool.Snapshot(2, 2, null).OldestQuarantineSeconds == 0, "empty quarantine");
        Task<JsonElement> removeFirst = pool.DestroyAsync(first);
        clock.Advance(20);
        Task<JsonElement> removeSecond = pool.DestroyAsync(second);
        clock.Advance(5);
        var both = pool.Snapshot(2, 2, null);
        Check(both.Quarantined == 2 && both.OldestQuarantineSeconds == 25, "oldest individual age");
        Check(both.Workers == 2 && both.ReservedMemoryMiB == 512, "pending removal retains capacity");
        first.Removal.SetResult(JsonSerializer.SerializeToElement(new { removed = true }));
        await removeFirst;
        Check(pool.Snapshot(2, 2, null).OldestQuarantineSeconds == 5, "completed oldest no longer contributes");
        second.Removal.SetException(new IOException("controlled removal failure"));
        try { await removeSecond; throw new Exception("Expected cleanup failure"); }
        catch (IOException) { }
        second.Removal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<JsonElement> retry = pool.DestroyAsync(second);
        clock.Advance(26);
        var stuck = pool.Snapshot(2, 2, null);
        Check(stuck.OldestQuarantineSeconds == 31 && stuck.Workers == 1, "retry preserves original age and reservation");
        second.Removal.SetResult(JsonSerializer.SerializeToElement(new { removed = true }));
        await retry;
        var empty = pool.Snapshot(2, 2, null);
        Check(empty.Quarantined == 0 && empty.OldestQuarantineSeconds == 0 && empty.ReservedMemoryMiB == 0, "confirmed cleanup resets age and capacity");
        return 6;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(long seconds) => _timestamp += seconds;
    }

    private sealed record ControlledProfile() : ExecutionProfile(256, TimeSpan.FromSeconds(5), null)
    {
        public override ExecutionProtections Protection => ExecutionProtections.None;
        internal override Task<ExecutionProfile> ResolveAsync(CancellationToken token) => Task.FromResult<ExecutionProfile>(this);
        internal override ExecutionProfile Normalize() => this;
        internal override Worker CreateWorker(string version, TimeProvider clock) => new ControlledWorker(this, version);
    }

    private sealed class ControlledWorker(ExecutionProfile profile, string version) : Worker(profile, version)
    {
        internal TaskCompletionSource<JsonElement> Removal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal override Stream Input => Stream.Null;
        internal override bool Running => true;
        internal override Task StartAsync(CancellationToken token) => Task.CompletedTask;
        internal override Task<JsonElement> DestroyAsync() => Removal.Task;
    }
}
