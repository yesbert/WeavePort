using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

internal static class SizeBoundaryChecks
{
    internal static async Task RunAsync()
    {
        var session = new EchoSession();
        await using var client = new LocalPluginClient(session);
        foreach (int bytes in new[] { (512 << 10) - 1, 512 << 10 })
        {
            var input = JsonSerializer.SerializeToElement(new string('x', bytes - 2));
            var output = await client.CallAsync("echo", input);
            if (output.GetString() != input.GetString())
            {
                throw new Exception("Boundary result changed.");
            }
        }
        foreach (string text in new[] { new string('x', (512 << 10) - 1), new string('<', 90000) })
        {
            int before = session.Invocations;
            try
            {
                await client.CallAsync("echo", JsonSerializer.SerializeToElement(text));
                throw new Exception("Oversized input accepted.");
            }
            catch (PluginCallException error) when (error.Status == "input-limit" && !error.MayHaveExecuted) { }
            if (session.Invocations != before)
            {
                throw new Exception("Oversized input dispatched.");
            }
        }
        session.LargeReply = true;
        try
        {
            await client.CallAsync("echo", JsonSerializer.SerializeToElement(1));
            throw new Exception("Oversized output accepted.");
        }
        catch (PluginCallException error) when (error.Status == "value-limit") { }
        Console.WriteLine("PASS: exact input/output limits, escaped UTF-8 size and pre-dispatch rejection");
    }

    private sealed class EchoSession : IPluginSession
    {
        public string Tenant => "size-boundary";
        public string Instance => "fixture";
        internal int Invocations { get; private set; }
        internal bool LargeReply { get; set; }
        public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Invocations++;
            JsonElement value = LargeReply ? JsonSerializer.SerializeToElement(new string('x', 512 << 10)) : payload.GetProperty("input");
            return Task.FromResult(new InvocationResult("ok", value, Instance, 0));
        }
        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
