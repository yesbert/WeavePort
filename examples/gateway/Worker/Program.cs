using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk;

await new PluginApplication()
    .Function<JsonElement, JsonElement>("echo", (input, _, _) => ValueTask.FromResult(input))
    .Function<BulkChunk, BulkChunk>("bulk-map", (input, _, _) => ValueTask.FromResult(input))
    .Stream<int, int>("numbers", Numbers)
    .RunAsync();

static async IAsyncEnumerable<int> Numbers(int count, PluginCallContext context, [EnumeratorCancellation] CancellationToken token)
{
    for (int i = 0; i < count; i++)
    {
        token.ThrowIfCancellationRequested();
        yield return i;
        await Task.Yield();
    }
}
internal sealed record BulkChunk(byte[] Data);
