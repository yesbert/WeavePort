using WeavePort.Abstractions;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class RegistryChecks
{
    internal static async Task RunAsync()
    {
        var registry = new GatewayRegistry();
        var clients = Enumerable.Range(0, 3).Select(_ => new TestClient(fail: true)).ToArray();
        string[] credentials = clients.Select(client => registry.Register(client, "test")).ToArray();
        Task disposal = registry.DisposeAsync().AsTask();
        await ExpectAsync<AggregateException>(() => disposal);
        Check(clients.All(client => client.Disposals == 1), "every throwing client disposed exactly once");
        Check(ReferenceEquals(disposal, registry.DisposeAsync().AsTask()), "disposal completion cached");
        foreach (string token in credentials)
        {
            await ExpectAsync<UnauthorizedAccessException>(() => Task.FromResult(registry.Get(token)));
            await ExpectAsync<UnauthorizedAccessException>(() => Task.FromResult(registry.Begin(token, Guid.NewGuid().ToString("N"), default)));
        }
        await ExpectAsync<ObjectDisposedException>(() => Task.FromResult(registry.Register(new TestClient(), "test")));
        await ClientCancellationAsync();
        await ActiveStreamAsync();
        await ConcurrentRevokeAsync();
        await RegistrationRaceAsync();
        Console.WriteLine("PASS: local client disposes its session after cancellation callback failure");
        Console.WriteLine("PASS: gateway all cleanup failures aggregated and admission closed");
        Console.WriteLine("PASS: active stream cancelled and awaited during shutdown");
        Console.WriteLine("PASS: pending revocation joined by registry shutdown");
        Console.WriteLine("PASS: concurrent registration has one ownership outcome");
    }

    private static async Task ClientCancellationAsync()
    {
        var session = new CancellationSession();
        var client = new LocalPluginClient(session);
        Task<JsonElement> call = client.CallAsync("probe", JsonSerializer.SerializeToElement(new
        {
        }));
        await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task disposal = client.DisposeAsync().AsTask();
        await ExpectAsync<AggregateException>(() => disposal.WaitAsync(TimeSpan.FromSeconds(2)));
        await ExpectAsync<OperationCanceledException>(() => call);
        Check(session.Disposals == 1, "owned session cleaned after client cancellation failure");
        Check(ReferenceEquals(disposal, client.DisposeAsync().AsTask()), "client disposal cached");
    }

    private static async Task ActiveStreamAsync()
    {
        var registry = new GatewayRegistry();
        var client = new TestClient();
        string token = registry.Register(client, "test");
        var stream = registry.Begin(token, Guid.NewGuid().ToString("N"), default);
        Task disposal = registry.DisposeAsync().AsTask();
        Check(stream.Stop.IsCancellationRequested && !disposal.IsCompleted, "shutdown waits for cancelled stream exit");
        registry.End(token, stream);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Check(client.Disposals == 1, "stream owner disposed once");
    }

    private static async Task ConcurrentRevokeAsync()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new GatewayRegistry();
        var client = new TestClient(release: release.Task);
        string token = registry.Register(client, "test");
        Task revoke = registry.RevokeAsync(token);
        Task dispose = registry.DisposeAsync().AsTask();
        Check(!dispose.IsCompleted, "registry joins pending revoke");
        release.SetResult();
        await Task.WhenAll(revoke, dispose).WaitAsync(TimeSpan.FromSeconds(2));
        Check(client.Disposals == 1, "concurrent revoke does not duplicate disposal");
    }

    private static async Task RegistrationRaceAsync()
    {
        for (int i = 0; i < 50; i++)
        {
            var registry = new GatewayRegistry();
            var client = new TestClient();
            bool accepted = false;
            await Task.WhenAll(Task.Run(() =>
            {
                try
                {
                    registry.Register(client, "test");
                    accepted = true;
                }
                catch (ObjectDisposedException) { /* Caller retains rejected ownership. */ }
            }), Task.Run(async () => await registry.DisposeAsync()));
            Check(client.Disposals == (accepted ? 1 : 0), "registration race ownership");
        }
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            throw new Exception(name);
        }
    }

    private sealed class CancellationSession : IPluginSession
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals { get; private set; }
        public string Tenant => "tenant";
        public string Instance => "fixture";
        public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // One callback owns both signals so a cancelled delay cannot unregister the throwing callback first.
            using var registration = cancellationToken.Register(() =>
            {
                cancelled.TrySetCanceled(cancellationToken);
                throw new IOException("cancel fixture");
            });
            Entered.TrySetResult();
            await cancelled.Task;
            throw new InvalidOperationException("Unreachable");
        }
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestClient(bool fail = false, Task? release = null) : IPluginClient
    {
        internal int Disposals { get; private set; }
        public Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            if (release is not null)
            {
                await release;
            }

            if (fail)
            {
                throw new IOException("cleanup fixture");
            }
        }
    }
}
