using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk;

if (args.Contains("--stream-worker"))
{
    await StreamFixture.RunAsync();
    return;
}
if (args.Contains("streams"))
{
    await StreamChecks.RunAsync(args);
    return;
}

if (args.Contains("--host-shared"))
{
    await SharedFixture.RunAsync(!args.Contains("--exclusive"));
    return;
}

if (args.Contains("collect"))
{
    await CollectionChecks.RunAsync(args);
    return;
}

var app = new PluginApplication { ConcurrentCalls = args.Contains("shared") };
app.Function<JsonElement, object>("stats", (_, _, _) => ValueTask.FromResult<object>(new { pid = Environment.ProcessId }));
app.Function<JsonElement, object>("identity", async (input, context, token) =>
{
    string tenant = context.Tenant;
    context.OnClose(async () => await Task.Delay(input.TryGetProperty("cleanupDelay", out var delay) ? delay.GetInt32() : 0, CancellationToken.None));
    // Cancellation must not acknowledge completion before this deliberately uncooperative work.
    await Task.Delay(input.GetProperty("delay").GetInt32(), CancellationToken.None);
    return new { tenant, current = context.Tenant };
});
app.Function<JsonElement, JsonElement>("callback", async (input, context, token) => await context.CallHostAsync("echo", input, token));
app.Function<JsonElement, JsonElement>("failure", (_, _, _) => throw new InvalidOperationException("Author failure"));
app.Function<JsonElement, JsonElement>("cleanupFailure", (input, context, _) =>
{
    context.OnClose(() => throw new InvalidOperationException("Cleanup failure"));
    return ValueTask.FromResult(input);
});
app.Stream<JsonElement, int>("live", Live);
app.Stream<JsonElement, int>("immediateCallback", ImmediateCallback);
app.Stream<JsonElement, object>("slow", StreamFixture.Slow);
app.Source<JsonElement>("bytes", (input, _, _) => ValueTask.FromResult<Stream>(new MemoryStream(Enumerable.Range(0, input.GetProperty("bytes").GetInt32()).Select(i => (byte)i).ToArray())));
app.Source<JsonElement>("large", (input, _, _) => ValueTask.FromResult<Stream>(new PatternSource(input.GetProperty("bytes").GetInt64())));
app.Source<JsonElement>("badDisposal", (_, _, _) => ValueTask.FromResult<Stream>(new FaultyDisposalSource()));
await app.RunAsync();

static async IAsyncEnumerable<int> Live(JsonElement input, PluginCallContext context, [EnumeratorCancellation] CancellationToken token)
{
    yield return 1;
    await Task.Delay(300, token);
    await context.CallHostAsync("echo", input, token);
    yield return 2;
}

static async IAsyncEnumerable<int> ImmediateCallback(JsonElement input, PluginCallContext context, [EnumeratorCancellation] CancellationToken token)
{
    yield return 1;
    // Closing the stream must revoke this callback even without an author cancellation token.
    await context.CallHostAsync("echo", input, CancellationToken.None);
    yield return 2;
}

internal sealed class FaultyDisposalSource : MemoryStream
{
    public override ValueTask DisposeAsync() => ValueTask.FromException(new IOException("Source disposal failed."));
}
