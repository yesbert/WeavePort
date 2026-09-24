using System.Text.Json;
using WeavePort.Abstractions;

internal sealed class NoCallbacks : IHostCallbacks
{
    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken) => throw new UnauthorizedAccessException();
}
