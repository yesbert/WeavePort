using System.Text.Json;
using WeavePort.Abstractions;

internal sealed class ReplySession(string tenant, JsonElement reply) : IPluginSession
{
    public string Tenant => tenant;
    public string Instance => "test";
    internal int Calls { get; private set; }
    public Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(new InvocationResult("ok", reply, Instance, 0));
    }
    public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
