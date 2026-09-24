using System.Runtime.CompilerServices;
using System.Text.Json;
using WeavePort.Sdk.Client;

internal sealed class EchoClient : IPluginClient
{
    public async Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default)
    {
        if (operation == "fail")
        {
            throw new PluginCallException("synthetic-failure", false);
        }

        if (operation == "parallel")
        {
            await Task.Delay(25, cancellationToken);
        }

        return input;
    }
    public async IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (operation == "wait")
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }

        yield return input;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
