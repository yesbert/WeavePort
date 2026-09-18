using WeavePort.Composition;
using WeavePort.Sdk.Client;
using System.Text.Json;

internal static class CompositionChecks
{
    internal static async Task RunAsync(string root)
    {
        await using (var scope = new ResultScope(root, "A", new(4096, 8192, 3)))
        {
            var input = await scope.CreateAsync((s, t) => s.WriteAsync(new byte[4096], t).AsTask());
            await using var foreign = new ResultScope(root, "A");
            await Check.Fails<UnauthorizedAccessException>(() => foreign.CopyToAsync(input, Stream.Null), "same-tenant foreign handle rejected");
            await Check.Fails<IOException>(() => scope.CreateAsync((s, t) => s.WriteAsync(new byte[4097], t).AsTask()), "object boundary enforced");
            Check.That(scope.ReservedBytes == 4096, "failed object releases reserved bytes");
            var fake = new BoundClient("B", Check.Json(new { data = "" }));
            await Check.Fails<UnauthorizedAccessException>(() => Composition.MapAsync(scope, input, fake), "SDK tenant mismatch before dispatch");
            Check.That(fake.Calls == 0, "foreign SDK operation never dispatched");
            foreach (var reply in new[] { Check.Json(new { data = "%%%" }), Check.Json(new { other = 1 }), Check.Json(new { data = 1 }), Check.Json(new { data = new byte[4097] }) })
            {
                await Check.Fails<InvalidDataException>(() => Composition.MapAsync(scope, input, new BoundClient("A", reply), 4096), "malformed SDK output rejected");
                Check.That(scope.ReservedBytes == 4096, "malformed output leaves no reservation");
            }
            await Check.Fails<ArgumentOutOfRangeException>(() => scope.TransformAsync(input, (b, _) => ValueTask.FromResult(b), 4095), "minimum block bound");
            await Check.Fails<ArgumentOutOfRangeException>(() => scope.TransformAsync(input, (b, _) => ValueTask.FromResult(b), 262145), "maximum block bound");
            var mapped = await Composition.MapAsync(scope, input, new BoundClient("A", Check.Json(new { data = new byte[4096] })), 4096);
            Check.That(mapped.Length == 4096 && scope.ReservedBytes == 8192, "exact scope byte limit accepted");
            await Check.Fails<IOException>(() => scope.CreateAsync((s, t) => s.WriteAsync(new byte[1], t).AsTask()), "scope boundary enforced");
        }
        await ConcurrentQuotaAsync(root);
        await CancellationFailureAsync(root);
        await FailedDeleteAsync(root);
        Check.That(!Directory.EnumerateFileSystemEntries(root).Any(), "all composition storage removed");
    }

    private static async Task ConcurrentQuotaAsync(string root)
    {
        await using var scope = new ResultScope(root, "A", new(16, 16, 2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scope.CreateAsync(async (s, t) => { await s.WriteAsync(new byte[16], t); entered.SetResult(); await release.Task; });
        await entered.Task;
        await Check.Fails<IOException>(() => scope.CreateAsync((s, t) => s.WriteAsync(new byte[1], t).AsTask()), "concurrent writes share scope quota");
        release.SetResult();
        await first;
        Check.That(scope.ReservedBytes == 16, "concurrent refusal preserves owner quota");
        await scope.CreateAsync((_, _) => Task.CompletedTask);
        await Check.Fails<IOException>(() => scope.CreateAsync((_, _) => Task.CompletedTask), "object count boundary enforced");
    }

    private static async Task CancellationFailureAsync(string root)
    {
        var scope = new ResultScope(root, "A");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = scope.CreateAsync(async (s, t) =>
        {
            using var registration = t.Register(() => throw new InvalidOperationException("cancellation callback"));
            await s.WriteAsync(new byte[1], t);
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, t);
        });
        await entered.Task;
        Task first = scope.DisposeAsync().AsTask();
        await Check.Fails<AggregateException>(() => first, "cancellation failure reported after cleanup");
        await Check.Fails<OperationCanceledException>(async () => await work, "producer drained despite callback failure");
        Check.That(ReferenceEquals(first, scope.DisposeAsync().AsTask()), "repeated disposal preserves completion");
        Check.That(scope.ReservedBytes == 0 && !Directory.EnumerateFileSystemEntries(root).Any(), "cleanup proceeds after cancellation callback failure");
    }

    private static async Task FailedDeleteAsync(string root)
    {
        // Deliberately replace the private output path with a directory, making
        // File.Delete fail independently of OS permission policy/root identity.
        var scope = new ResultScope(root, "A");
        string directory = Directory.EnumerateDirectories(root).Single();
        var error = await Check.Fails<AggregateException>(() => scope.CreateAsync(async (s, t) =>
        {
            await s.WriteAsync(new byte[8], t);
            string file = Directory.EnumerateFiles(directory).Single();
            File.Move(file, file + ".saved");
            Directory.CreateDirectory(file);
            throw new InvalidOperationException("production failure");
        }), "production and partial deletion failures aggregated");
        Check.That(error.InnerExceptions.Count == 2 && scope.ReservedBytes == 8, "failed delete keeps conservative reservation");
        await scope.DisposeAsync();
        Check.That(scope.ReservedBytes == 0, "scope cleanup removes failed partial result");

        scope = new ResultScope(root, "A");
        directory = Directory.EnumerateDirectories(root).Single();
        Directory.Delete(directory);
        await File.WriteAllTextAsync(directory, "not a directory");
        Task disposal = scope.DisposeAsync().AsTask();
        await Check.Fails<AggregateException>(() => disposal, "scope delete failure remains observable");
        Check.That(ReferenceEquals(disposal, scope.DisposeAsync().AsTask()), "failed disposal result cached");
        File.Delete(directory);
    }

    private sealed class BoundClient(string tenant, JsonElement reply) : IBoundPluginClient
    {
        internal int Calls { get; private set; }
        public Task<string> GetTenantAsync(CancellationToken cancellationToken = default) => Task.FromResult(tenant);
        public Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(reply); }
        public IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
