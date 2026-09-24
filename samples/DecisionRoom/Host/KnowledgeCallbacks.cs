using System.Text.Json;
using DecisionRoom.Contracts;
using WeavePort.Abstractions;

namespace DecisionRoom.Host;

internal sealed class KnowledgeCallbacks(RunConfiguration config) : IHostCallbacks
{
    internal int Calls { get; private set; }

    public ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (call.Operation != "knowledge.read" || call.Context.Tenant != config.Tenant || !config.Knowledge.TryGetValue(call.Context.Profile, out var risks))
        {
            throw new UnauthorizedAccessException("Knowledge scope refused.");
        }

        Calls++;
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new Knowledge(config.Tenant + "/" + call.Context.Profile, risks), Wire.Json));
    }
}
