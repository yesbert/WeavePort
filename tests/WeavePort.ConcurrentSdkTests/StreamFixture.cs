using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk;

internal static class StreamFixture
{
    internal static Task RunAsync() => new PluginApplication()
        .Function<JsonElement, object>("echo", async (input, context, token) =>
        {
            await Task.Delay(input.TryGetProperty("delay", out var delay) ? delay.GetInt32() : 0, token);
            return new { pid = Environment.ProcessId, tenant = context.Tenant };
        })
        .Stream<JsonElement, object>("slow", Slow)
        .RunAsync();

    internal static async IAsyncEnumerable<object> Slow(JsonElement input, PluginCallContext context, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Delay(input.TryGetProperty("firstDelay", out var first) ? first.GetInt32() : 0, token);
        yield return new { pid = Environment.ProcessId, item = 1 };
        await Task.Delay(input.TryGetProperty("nextDelay", out var next) ? next.GetInt32() : 300, token);
        yield return new { pid = Environment.ProcessId, item = 2 };
    }
}
