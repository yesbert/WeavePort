using System.Text.Json;

namespace WeavePort.Abstractions;
/// <summary>Dispatches an authorized callback; implementations must observe cancellation.</summary>
public interface IHostCallbacks
{
    /// <summary>Executes a callback with host-owned identity and a bounded lifetime.</summary>
    ValueTask<JsonElement> InvokeAsync(HostCall call, CancellationToken cancellationToken);
}
