using System.Reflection;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

internal static class ClientDisposalChecks
{
    internal static async Task RunAsync()
    {
        await PausedStreamAsync(fail: false);
        await PausedStreamAsync(fail: true);
        await ActiveCallAsync();
        Console.WriteLine("PASS client disposal, active calls, paused streams and cleanup failure");
    }
    private static async Task PausedStreamAsync(bool fail)
    {
        var session = new Session(fail);
        var client = new LocalPluginClient(session);
        var source = (CancellationTokenSource)typeof(LocalPluginClient).GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;
        await using var iterator = client.StreamAsync("items", JsonSerializer.SerializeToElement(1)).GetAsyncEnumerator();
        if (!await iterator.MoveNextAsync())
        {
            throw new Exception("Missing first item");
        }

        await AssertDisposalAsync(client, fail);
        await AssertDisposalAsync(client, fail);
        if (session.Disposals != 1)
        {
            throw new Exception("Duplicate binding cleanup");
        }

        try
        {
            _ = source.Token;
            throw new Exception("client source not disposed");
        }
        catch (ObjectDisposedException) { }
        int calls = session.Invocations;
        try
        {
            await iterator.MoveNextAsync();
            throw new Exception("Late stream must cancel");
        }
        catch (OperationCanceledException) { }
        if (session.Invocations != calls)
        {
            throw new Exception("Late stream dispatched after disposal");
        }
    }

    private static async Task AssertDisposalAsync(LocalPluginClient client, bool expectFailure)
    {
        try
        {
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException) when (expectFailure)
        {
            return;
        }

        if (expectFailure)
        {
            throw new Exception("Missing failure");
        }
    }

    private static async Task ActiveCallAsync()
    {
        var session = new WaitingSession();
        var client = new LocalPluginClient(session);
        Task<JsonElement> call = client.CallAsync("wait", JsonSerializer.SerializeToElement(1));
        await session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await client.DisposeAsync();
        try
        {
            await call;
            throw new Exception("Active call was not cancelled");
        }
        catch (OperationCanceledException) { }
        try
        {
            await client.CallAsync("late", JsonSerializer.SerializeToElement(1));
            throw new Exception("Late call admitted");
        }
        catch (ObjectDisposedException) { }
        if (session.Invocations != 1)
        {
            throw new Exception("Late call dispatched");
        }
    }
    private sealed class WaitingSession : IPluginSession
    {
        public string Tenant => "a";
        public string Instance => "wait";
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Invocations { get; private set; }
        public async Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Invocations++;
            Entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new Exception("Delay completed without cancellation");
        }
        public Task RestartAsync(CancellationToken cancellationToken = default) => throw new Exception("Unexpected restart");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Session(bool fail) : IPluginSession
    {
        public string Tenant => "a";
        public string Instance => "test";
        internal int Disposals { get; private set; }
        internal int Invocations { get; private set; }
        public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Invocations++;
            object value = operation == "$sdk.start" ? new
            {
                stream = "s"
            } : new
            {
                items = new[] { 1 },
                done = false
            };
            return Task.FromResult(new InvocationResult("ok", JsonSerializer.SerializeToElement(value), Instance, 0));
        }
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return fail ? ValueTask.FromException(new IOException("synthetic")) : ValueTask.CompletedTask;
        }
    }
}
