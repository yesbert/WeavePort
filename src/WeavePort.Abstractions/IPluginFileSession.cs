using System.Text.Json;

namespace WeavePort.Abstractions;
/// <summary>A binding that can invoke functions with named inputs and outputs.</summary>
public interface IPluginFileSession : IPluginSession
{
    /// <summary>Invokes a function with files. A binding or worker without file support returns files-unsupported before dispatch.</summary>
    Task<InvocationResult> InvokeAsync(string operation, JsonElement payload, PluginFiles files, CancellationToken cancellationToken = default);
}
