using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

internal static class ClientOperationChecks
{
    public static async Task RunAsync()
    {
        var session = new OperationSession();
        await using var client = new LocalPluginClient(session);
        await using (var iterator = client.StreamAsync("rows", JsonSerializer.SerializeToElement(new
        {
        })).GetAsyncEnumerator())
        {
            if (!await iterator.MoveNextAsync() || !session.Held || iterator.Current.GetInt32() != 42)
            {
                throw new InvalidOperationException("Stream heartbeat/residency contract failed.");
            }
        }
        if (session.Held || !session.Closed)
        {
            throw new InvalidOperationException("Early disposal did not close before releasing residency.");
        }

        session.SupportsStreaming = false;
        int before = session.Calls;
        try
        {
            await foreach (var row in client.StreamAsync("rows", JsonSerializer.SerializeToElement(new
            {
            })))
            {
            }
            throw new InvalidOperationException("Shared stream should be refused.");
        }
        catch (NotSupportedException) { }
        if (session.Calls != before)
        {
            throw new InvalidOperationException("Shared stream was dispatched before refusal.");
        }

        Console.WriteLine("PASS: client operation lease, heartbeat and shared refusal");
    }

    private sealed class OperationSession : IPluginOperationSession
    {
        public bool SupportsStreaming { get; set; } = true;
        public bool Held { get; private set; }
        public bool Closed { get; private set; }
        public int Calls { get; private set; }
        private int _next;
        public string Tenant => "test";
        public string Instance => "fake";
        public ValueTask<IPluginSession> AcquireOperationAsync(CancellationToken cancellationToken = default)
        {
            Held = true;
            return ValueTask.FromResult<IPluginSession>(this);
        }
        public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (!Held)
            {
                throw new InvalidOperationException("Missing operation residency.");
            }

            object result = operation switch
            {
                "$sdk.start" => new { stream = "1" },
                "$sdk.next" when _next++ == 0 => new { items = Array.Empty<int>(), done = false },
                "$sdk.next" => new { items = new[] { 42 }, done = false },
                "$sdk.close" => Close(),
                _ => throw new InvalidOperationException(operation)
            };
            return Task.FromResult(new InvocationResult("ok", JsonSerializer.SerializeToElement(result), Instance, 0));
        }
        private object Close()
        {
            Closed = true;
            return new
            {
            };
        }
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Held = false;
            return ValueTask.CompletedTask;
        }
    }
}
