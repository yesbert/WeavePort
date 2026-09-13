using System.Text.Json;
using WeavePort.Composition;
using WeavePort.Abstractions;

internal static class BulkChecks
{
    internal static async Task RunAsync(string config, string output)
    {
        string root = Path.Combine(output, "checks-store");
        var passed = new List<string>();
        async Task Denied(Func<Task> action, string name)
        {
            try { await action(); throw new InvalidOperationException("Expected rejection: " + name); }
            catch (Exception e) when (e is UnauthorizedAccessException or InvalidDataException or IOException or OperationCanceledException or ObjectDisposedException) { passed.Add(name); }
        }
        await using (var owner = new ResultScope(root, "A"))
        await using (var foreign = new ResultScope(root, "B"))
        await using (var otherRequest = new ResultScope(root, "A"))
        {
            ResultHandle input = await BulkScenario.GenerateAsync(owner, 1 << 20);
            await Denied(() => foreign.CopyToAsync(input, Stream.Null), "cross-tenant-reference");
            await Denied(() => otherRequest.CopyToAsync(input, Stream.Null), "same-tenant-other-request-reference");
            using var original = new CheckingSink(BulkScenario.Pattern(false));
            await owner.CopyToAsync(input, original);
            if (original.Count != 1 << 20) throw new InvalidDataException();
            passed.Add("immutable-source-integrity");
            var fake = new ReplySession("B", JsonSerializer.SerializeToElement(new { data = "" }));
            await Denied(() => Composition.MapAsync(owner, input, fake), "wrong-plugin-tenant-rejected");
            if (fake.Calls != 0) throw new InvalidOperationException("Foreign plugin invoked.");
            var oversized = new ReplySession("A", JsonSerializer.SerializeToElement(new { data = Convert.ToBase64String(new byte[65537]) }));
            await Denied(() => Composition.MapAsync(owner, input, oversized), "oversized-plugin-output-rejected");
            var blocking = new BlockingSink();
            var copyCancellation = new CancellationTokenSource();
            Task copying = owner.CopyToAsync(input, blocking, copyCancellation.Token);
            await blocking.Started.Task;
            if (copying.IsCompleted || blocking.Writes != 1) throw new InvalidOperationException("Caller backpressure ignored.");
            copyCancellation.Cancel();
            await Denied(async () => await copying, "slow-caller-cancellation");
            copyCancellation.Dispose();
            long before = owner.ReservedBytes;
            await Denied(() => owner.TransformAsync(input, (_, _) => throw new IOException("synthetic")), "failed-transform");
            if (owner.ReservedBytes != before) throw new InvalidOperationException("Partial reservation leaked.");
            passed.Add("failed-transform-releases-reservation");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Denied(() => owner.CopyToAsync(input, Stream.Null, cancelled.Token), "cancelled-delivery");
            int running = 0, peak = 0;
            var branches = Enumerable.Range(0, 6).Select<int, Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>>(_ => async (scope, value, token) =>
            {
                int active = Interlocked.Increment(ref running);
                int old; do { old = peak; if (old >= active) break; } while (Interlocked.CompareExchange(ref peak, active, old) != old);
                try { await Task.Delay(10, token); return value; } finally { Interlocked.Decrement(ref running); }
            }).ToArray();
            ResultHandle[] outputs = await Composition.FanOutAsync(owner, input, branches, 2);
            if (peak != 2 || running != 0 || outputs.Any(x => !ReferenceEquals(x, input))) throw new InvalidOperationException("Parallel bound or order.");
            passed.Add("fan-out-bound-and-order");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool siblingCancelled = false;
            Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>[] failing = [
                async (_, _, token) => { started.SetResult(); try { await Task.Delay(Timeout.Infinite, token); } catch (OperationCanceledException) { siblingCancelled = true; throw; } return input; },
                async (_, _, _) => { await started.Task; throw new IOException("required branch failed"); }
            ];
            await Denied(() => Composition.FanOutAsync(owner, input, failing, 2), "required-branch-failure");
            if (!siblingCancelled) throw new InvalidOperationException("Sibling was not awaited/cancelled.");
            passed.Add("failed-fan-out-cancels-and-awaits-sibling");
            ResultHandle joined = await Composition.ConcatenateAsync(owner, [input, input]);
            using var joinedCaller = new CheckingSink(BulkScenario.Pattern(false)); await owner.CopyToAsync(joined, joinedCaller);
            if (joinedCaller.Count != 2 << 20) throw new InvalidDataException(); passed.Add("ordered-fan-in-full-caller-delivery");
        }
        await using (var quota = new ResultScope(root, "quota", new ResultLimits(128, 128, 1)))
        {
            await Denied(() => quota.CreateAsync(async (stream, token) => await stream.WriteAsync(new byte[129], token)), "object-byte-quota");
            if (quota.ReservedBytes != 0) throw new InvalidOperationException();
            await quota.CreateAsync(async (stream, token) => await stream.WriteAsync(new byte[128], token));
            await Denied(() => quota.CreateAsync((_, _) => Task.CompletedTask), "object-count-quota");
        }
        await using (var quota = new ResultScope(root, "quota", new ResultLimits(128, 192, 8)))
        {
            await quota.CreateAsync(async (s, t) => await s.WriteAsync(new byte[128], t));
            await Denied(() => quota.CreateAsync(async (s, t) => await s.WriteAsync(new byte[128], t)), "scope-byte-quota");
            if (quota.ReservedBytes != 128) throw new InvalidOperationException();
        }
        await using (var arguments = new ResultScope(root, "arguments", new ResultLimits(64, 64, 2)))
        {
            ResultHandle valid = await arguments.CreateAsync((stream, _) =>
            {
                try { stream.Write(new byte[1], 0, -1); throw new InvalidOperationException("Invalid write accepted."); }
                catch (ArgumentOutOfRangeException) { }
                stream.Write(new byte[64], 0, 64);
                return Task.CompletedTask;
            });
            if (valid.Length != 64 || arguments.ReservedBytes != 64) throw new InvalidOperationException("Invalid write corrupted quota.");
            passed.Add("invalid-write-does-not-corrupt-quota");
        }
        var disposal = new ResultScope(root, "cancel");
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ResultHandle> pending = disposal.CreateAsync(async (stream, token) => { await stream.WriteAsync(new byte[64], token); writing.SetResult(); await Task.Delay(Timeout.Infinite, token); });
        await writing.Task; await disposal.DisposeAsync();
        await Denied(async () => await pending, "dispose-cancels-active-production");
        await Denied(() => disposal.CreateAsync((_, _) => Task.CompletedTask), "disposed-scope-rejected");
        await using (var scenario = await BulkScenario.CreateAsync(config, root, "multilingual"))
        {
            await scenario.RunAsync(8 << 20, false, 65536); passed.Add("multilingual-serial-8MiB-caller-integrity");
            await scenario.RunAsync(8 << 20, true, 65536); passed.Add("multilingual-fan-out-fan-in-24MiB-caller-integrity");
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            await Denied(() => scenario.RunAsync(32 << 20, true, 65536, token: cancelled.Token), "cancelled-plugin-composition");
            await scenario.RunAsync(256 << 10, true, 65536); passed.Add("recovery-after-cancellation");
        }
        if (Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Scope files leaked.");
        passed.Add("all-request-files-removed");
        await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { passed, failures = 0 }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Bulk verification: {passed.Count} passed.");
    }
}

internal sealed class ReplySession(string tenant, JsonElement reply) : IPluginSession
{
    public string Tenant => tenant;
    public string Instance => "test";
    internal int Calls { get; private set; }
    public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(new InvocationResult("ok", reply, Instance, 0)); }
    public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class BlockingSink : Stream
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Writes { get; private set; }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { Writes++; Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
