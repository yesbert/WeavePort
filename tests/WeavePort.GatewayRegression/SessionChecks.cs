using System.Collections.Concurrent;
using System.Text.Json;
using WeavePort.Sdk.Client;
using WeavePort.Sdk.Gateway;

internal static class SessionChecks
{
    internal static async Task<object> RunAsync(RemotePluginClient client, GatewayRegistry registry, Uri address, ConcurrentQueue<object> requests)
    {
        // A quiet session must survive beyond Kestrel's default data-rate grace.
        await Task.Delay(TimeSpan.FromSeconds(12));
        JsonElement idle = await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
        {
            value = "after-idle"
        }));
        if (idle.GetProperty("value").GetString() != "after-idle")
        {
            throw new InvalidDataException("Idle session result.");
        }

        bool errorObserved = false;
        try
        {
            await client.CallAsync("fail", JsonSerializer.SerializeToElement(new
            {
            }));
        }
        catch (PluginCallException error) when (error.Status == "unknown-error" && !error.MayHaveExecuted) { errorObserved = true; }
        if (!errorObserved)
        {
            throw new InvalidDataException("Missing failure metadata.");
        }

        await Task.WhenAll(Enumerable.Range(0, 32).Select(async index =>
                {
                    var value = await client.CallAsync("parallel", JsonSerializer.SerializeToElement(new
                    {
                        index
                    }));
                    if (value.GetProperty("index").GetInt32() != index)
                    {
                        throw new InvalidDataException("Crossed parallel replies.");
                    }
                }));
        int sessions = requests.Count;
        if (sessions is < 2 or > 8)
        {
            throw new InvalidDataException("Session concurrency/retention bound.");
        }

        bool revocationObserved = await CheckRevocationAsync(registry, address);

        await client.CallAsync("echo", JsonSerializer.SerializeToElement(new
        {
        }));
        bool queuedCancellation = await CheckQueueAsync(registry, address);
        return new
        {
            queuedCancellation,
            idleSeconds = 12,
            parallelCalls = 32,
            sessions,
            errorObserved,
            revocationObserved,
            unaffectedBinding = true
        };
    }
    private static async Task<bool> CheckRevocationAsync(GatewayRegistry registry, Uri address)
    {
        string revoked = registry.Register(new EchoClient(), "test");
        await using var other = new RemotePluginClient(address, revoked, TimeSpan.FromSeconds(1));
        await other.CallAsync("echo", JsonSerializer.SerializeToElement(new
        {
        }));
        await registry.RevokeAsync(revoked);
        bool revocationObserved = false;
        try
        {
            await other.CallAsync("echo", JsonSerializer.SerializeToElement(new
            {
            }));
        }
        catch (PluginCallException error) when (error.Status == "binding-denied" && !error.MayHaveExecuted) { revocationObserved = true; }
        if (!revocationObserved)
        {
            throw new InvalidDataException("Opened session bypassed revocation.");
        }

        return revocationObserved;
    }

    private static async Task<bool> CheckQueueAsync(GatewayRegistry registry, Uri address)
    {
        var held = new HeldClient();
        string credential = registry.Register(held, "test");
        await using var client = new RemotePluginClient(address, credential, TimeSpan.FromSeconds(5));
        Task<JsonElement>[] active = Enumerable.Range(0, 8)
            .Select(index => client.CallAsync("hold", JsonSerializer.SerializeToElement(new { index }))).ToArray();
        try
        {
            await held.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            bool cancelled = false;
            try
            {
                await client.CallAsync("queued", JsonSerializer.SerializeToElement(new
                {
                }), cancel.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled || held.Count != 8)
            {
                throw new InvalidDataException("Queued cancellation dispatched or failed to cancel.");
            }

            held.Release.TrySetResult();
            JsonElement[] results = await Task.WhenAll(active);
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].GetProperty("index").GetInt32() != i)
                {
                    throw new InvalidDataException("Queued cancellation crossed replies.");
                }
            }

            await client.CallAsync("after-queue", JsonSerializer.SerializeToElement(new
            {
            }));
            return true;
        }
        finally
        {
            held.Release.TrySetResult();
            await Task.WhenAll(active);
            await registry.RevokeAsync(credential);
        }
    }

    private sealed class HeldClient : IPluginClient
    {
        internal int Count;
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref Count) == 8)
            {
                Ready.TrySetResult();
            }

            await Release.Task.WaitAsync(cancellationToken);
            return input;
        }
        public IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

}
