using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk;

var example = new Example();
await new PluginApplication()
    .Function<JsonElement, JsonElement>("echo", (input, _, _) => ValueTask.FromResult(input))
    .Function<CallbackRequest, JsonElement>("owner", (input, context, token) => new(context.CallHostAsync(input.Operation, input.Input, token)))
    .Function<JsonElement, object>("state", (_, _, _) => ValueTask.FromResult<object>(new { closed = example.Closed }))
    .Function<JsonElement, object>("crash", (_, _, _) => { Environment.Exit(17); return ValueTask.FromResult<object>(new { }); })
    .Stream<Query, Row>("records", example.RecordsAsync)
    .RunAsync();

internal sealed record CallbackRequest(string Operation, JsonElement Input);
internal sealed record Query(int Count, int Width, int DelayMs = 0, int FailAt = -1, bool Callbacks = false);
internal sealed record Row(int Id, string Text, string Owner);
internal sealed class Example
{
    internal int Closed { get; private set; }
    internal async IAsyncEnumerable<Row> RecordsAsync(Query query, PluginCallContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.Count is < 0 or > 1_000_000 || query.Width is < 0 or > 200_000 || query.DelayMs is < 0 or > 1000) throw new ArgumentException("Invalid query.");
        try
        {
            for (int i = 0; i < query.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i == query.FailAt) throw new InvalidOperationException("Fixture failure.");
                if (query.DelayMs > 0) await Task.Delay(query.DelayMs, cancellationToken);
                string owner = context.Tenant;
                if (query.Callbacks && i % 4 == 0)
                    owner = (await context.CallHostAsync("host.owner", JsonSerializer.SerializeToElement(new { tenant = "forged" }), cancellationToken)).GetProperty("owner").GetString()!;
                yield return new Row(i, new string('x', query.Width), owner);
            }
        }
        finally { Closed++; }
    }
}
