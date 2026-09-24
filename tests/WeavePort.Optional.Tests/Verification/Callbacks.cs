using System.Text.Json;
using WeavePort.Abstractions;

internal sealed class Callbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken token) => throw new UnauthorizedAccessException();
}
