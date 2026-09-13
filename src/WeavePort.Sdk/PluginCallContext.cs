using System.Text.Json;

namespace WeavePort.Sdk;
/// <summary>Invocation context supplied by the host; payload values never establish callback authority.</summary>
public sealed class PluginCallContext
{
    private readonly Func<string, JsonElement, CancellationToken, Task<JsonElement>> _callback;
    internal PluginCallContext(JsonElement context, Func<string, JsonElement, CancellationToken, Task<JsonElement>> callback)
    {
        Tenant = context.GetProperty("tenant").GetString()!;
        Configuration = context.GetProperty("configuration");
        _callback = callback;
    }

    /// <summary>The host-bound customer identity, for application context rather than authorization decisions.</summary>
    public string Tenant { get; }
    /// <summary>Configuration for this worker binding.</summary>
    public JsonElement Configuration { get; }

    /// <summary>Calls a capability; the host independently checks its grant and binding authority.</summary>
    public Task<JsonElement> CallHostAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => _callback(operation, input, cancellationToken);
}
