using System.Text.Json;

namespace WeavePort.Sdk.Client;
/// <summary>Product-facing calls independent of local or worker-host deployment.</summary>
public interface IPluginClient : IAsyncDisposable
{
    /// <summary>Invokes an ordinary function and returns its complete bounded JSON value.</summary>
    Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default);
    /// <summary>Invokes a function with per-call host timing. Legacy implementations return unavailable timing.</summary>
    async Task<PluginCallResult<JsonElement>> CallWithMetadataAsync(string operation, JsonElement input, CancellationToken cancellationToken = default) => new(await CallAsync(operation, input, cancellationToken), null);
    /// <summary>Invokes a function with named inputs and outputs. Clients without file support fail with files-unsupported before dispatch.</summary>
    Task<PluginCallResult<JsonElement>> CallWithFilesAsync(string operation, JsonElement input, WeavePort.Abstractions.PluginFiles files, CancellationToken cancellationToken = default) => Task.FromException<PluginCallResult<JsonElement>>(new PluginCallException(WeavePort.Internal.FailureCodes.FilesUnsupported, mayHaveExecuted: false));
    /// <summary>Enumerates results incrementally; exceptions after items mean the stream did not complete successfully.</summary>
    IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default);
}
