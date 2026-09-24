using System.Text.Json;
using WeavePort.Composition;
using WeavePort.Abstractions;

internal sealed class BulkChecks(string root)
{
    private readonly List<string> _passed = [];

    internal static Task RunAsync(string config, string output) =>
        new BulkChecks(Path.Combine(output, "checks-store")).RunAllAsync(config, output);

    private async Task RunAllAsync(string config, string output)
    {
        await VerifyScopeIsolationAsync();
        await VerifyQuotasAsync();
        await VerifyDisposalAsync();
        await VerifyMultilingualAsync(config);
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException("Scope files leaked.");
        }

        _passed.Add("all-request-files-removed");
        await File.WriteAllTextAsync(Path.Combine(output, "verification.json"),
            JsonSerializer.Serialize(new
            {
                passed = _passed,
                failures = 0
            }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Bulk verification: {_passed.Count} passed.");
    }

    private async Task DeniedAsync(Func<Task> action, string name)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidDataException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            _passed.Add(name);
            return;
        }

        throw new InvalidOperationException("Expected rejection: " + name);
    }

    private async Task VerifyScopeIsolationAsync()
    {
        await using var owner = new ResultScope(root, "A");
        await using var foreign = new ResultScope(root, "B");
        await using var otherRequest = new ResultScope(root, "A");
        ResultHandle input = await BulkScenario.GenerateAsync(owner, 1 << 20);
        await DeniedAsync(() => foreign.CopyToAsync(input, Stream.Null), "cross-tenant-reference");
        await DeniedAsync(() => otherRequest.CopyToAsync(input, Stream.Null), "same-tenant-other-request-reference");
        using var original = new CheckingSink(BulkScenario.Pattern(false));
        await owner.CopyToAsync(input, original);
        if (original.Count != 1 << 20)
        {
            throw new InvalidDataException();
        }

        _passed.Add("immutable-source-integrity");
        var fake = new ReplySession("B", JsonSerializer.SerializeToElement(new
        {
            data = ""
        }));
        await DeniedAsync(() => Composition.MapAsync(owner, input, fake), "wrong-plugin-tenant-rejected");
        if (fake.Calls != 0)
        {
            throw new InvalidOperationException("Foreign plugin invoked.");
        }

        var oversized = new ReplySession("A", JsonSerializer.SerializeToElement(new
        {
            data = Convert.ToBase64String(new byte[65537])
        }));
        await DeniedAsync(() => Composition.MapAsync(owner, input, oversized), "oversized-plugin-output-rejected");
        await VerifyDeliveryFailureAsync(owner, input);
        await VerifyFanOutAsync(owner, input);
        ResultHandle joined = await Composition.ConcatenateAsync(owner, [input, input]);
        using var joinedCaller = new CheckingSink(BulkScenario.Pattern(false));
        await owner.CopyToAsync(joined, joinedCaller);
        if (joinedCaller.Count != 2 << 20)
        {
            throw new InvalidDataException();
        }

        _passed.Add("ordered-fan-in-full-caller-delivery");
    }

    private async Task VerifyDeliveryFailureAsync(ResultScope owner, ResultHandle input)
    {
        var blocking = new BlockingSink();
        var copyCancellation = new CancellationTokenSource();
        Task copying = owner.CopyToAsync(input, blocking, copyCancellation.Token);
        await blocking.Started.Task;
        if (copying.IsCompleted || blocking.Writes != 1)
        {
            throw new InvalidOperationException("Caller backpressure ignored.");
        }

        copyCancellation.Cancel();
        await DeniedAsync(async () => await copying, "slow-caller-cancellation");
        copyCancellation.Dispose();
        long before = owner.ReservedBytes;
        await DeniedAsync(() => owner.TransformAsync(input, (_, _) => throw new IOException("synthetic")), "failed-transform");
        if (owner.ReservedBytes != before)
        {
            throw new InvalidOperationException("Partial reservation leaked.");
        }

        _passed.Add("failed-transform-releases-reservation");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await DeniedAsync(() => owner.CopyToAsync(input, Stream.Null, cancelled.Token), "cancelled-delivery");

    }

    private async Task VerifyFanOutAsync(ResultScope owner, ResultHandle input)
    {
        int running = 0, peak = 0;
        var branches = Enumerable.Range(0, 6).Select<int, Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>>(_ => async (scope, value, token) =>
        {
            int active = Interlocked.Increment(ref running);
            int old;
            do
            {
                old = peak;
                if (old >= active)
                {
                    break;
                }
            } while (Interlocked.CompareExchange(ref peak, active, old) != old);
            try
            {
                await Task.Delay(10, token);
                return value;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }).ToArray();
        ResultHandle[] outputs = await Composition.FanOutAsync(owner, input, branches, 2);
        if (peak != 2 || running != 0 || outputs.Any(x => !ReferenceEquals(x, input)))
        {
            throw new InvalidOperationException("Parallel bound or order.");
        }

        _passed.Add("fan-out-bound-and-order");
        await VerifyFanOutFailureAsync(owner, input);
    }

    private async Task VerifyFanOutFailureAsync(ResultScope owner, ResultHandle input)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool siblingCancelled = false;
        Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>[] failing =
        [
            async (_, _, token) =>
            {
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    siblingCancelled = true;
                    throw;
                }

                return input;
            },
            async (_, _, _) =>
            {
                await started.Task;
                throw new IOException("required branch failed");
            }
        ];
        await DeniedAsync(() => Composition.FanOutAsync(owner, input, failing, 2), "required-branch-failure");
        if (!siblingCancelled)
        {
            throw new InvalidOperationException("Sibling was not awaited/cancelled.");
        }

        _passed.Add("failed-fan-out-cancels-and-awaits-sibling");

    }

    private async Task VerifyQuotasAsync()
    {
        await using (var quota = new ResultScope(root, "quota", new ResultLimits(128, 128, 1)))
        {
            await DeniedAsync(() => quota.CreateAsync(async (stream, token) => await stream.WriteAsync(new byte[129], token)), "object-byte-quota");
            if (quota.ReservedBytes != 0)
            {
                throw new InvalidOperationException();
            }

            await quota.CreateAsync(async (stream, token) => await stream.WriteAsync(new byte[128], token));
            await DeniedAsync(() => quota.CreateAsync((_, _) => Task.CompletedTask), "object-count-quota");
        }
        await using (var quota = new ResultScope(root, "quota", new ResultLimits(128, 192, 8)))
        {
            await quota.CreateAsync(async (s, t) => await s.WriteAsync(new byte[128], t));
            await DeniedAsync(() => quota.CreateAsync(async (s, t) => await s.WriteAsync(new byte[128], t)), "scope-byte-quota");
            if (quota.ReservedBytes != 128)
            {
                throw new InvalidOperationException();
            }
        }
        await using (var arguments = new ResultScope(root, "arguments", new ResultLimits(64, 64, 2)))
        {
            ResultHandle valid = await arguments.CreateAsync((stream, _) =>
            {
                try
                {
                    stream.Write(new byte[1], 0, -1);
                    throw new InvalidOperationException("Invalid write accepted.");
                }
                catch (ArgumentOutOfRangeException) { }
                stream.Write(new byte[64], 0, 64);
                return Task.CompletedTask;
            });
            if (valid.Length != 64 || arguments.ReservedBytes != 64)
            {
                throw new InvalidOperationException("Invalid write corrupted quota.");
            }

            _passed.Add("invalid-write-does-not-corrupt-quota");
        }

    }

    private async Task VerifyDisposalAsync()
    {
        var disposal = new ResultScope(root, "cancel");
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ResultHandle> pending = disposal.CreateAsync(async (stream, token) =>
        {
            await stream.WriteAsync(new byte[64], token);
            writing.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        });
        await writing.Task;
        await disposal.DisposeAsync();
        await DeniedAsync(async () => await pending, "dispose-cancels-active-production");
        await DeniedAsync(() => disposal.CreateAsync((_, _) => Task.CompletedTask), "disposed-scope-rejected");

    }

    private async Task VerifyMultilingualAsync(string config)
    {
        await using (var scenario = await BulkScenario.CreateAsync(config, root, "multilingual"))
        {
            await scenario.RunAsync(8 << 20, false, 65536);
            _passed.Add("multilingual-serial-8MiB-caller-integrity");
            await scenario.RunAsync(8 << 20, true, 65536);
            _passed.Add("multilingual-fan-out-fan-in-24MiB-caller-integrity");
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            await DeniedAsync(() => scenario.RunAsync(32 << 20, true, 65536, token: cancelled.Token), "cancelled-plugin-composition");
            await scenario.RunAsync(256 << 10, true, 65536);
            _passed.Add("recovery-after-cancellation");
        }

    }
}
