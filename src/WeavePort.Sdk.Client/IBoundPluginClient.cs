using WeavePort.Internal;

namespace WeavePort.Sdk.Client;
/// <summary>A client whose immutable tenant identity comes from its trusted host binding.</summary>
public interface IBoundPluginClient : IPluginClient
{
    /// <summary>Reads bounded source blocks; dispose enumeration to close the source and release residency.</summary>
    IAsyncEnumerable<ReadOnlyMemory<byte>> SourceAsync(string operation, System.Text.Json.JsonElement input, int chunkBytes = ProtocolLimits.SourceChunkDefaultBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException("This client does not support binary sources.");
    /// <summary>Resolves authenticated binding identity without invoking plugin code. Custom implementations are trusted.</summary>
    Task<string> GetTenantAsync(CancellationToken cancellationToken = default);
}
