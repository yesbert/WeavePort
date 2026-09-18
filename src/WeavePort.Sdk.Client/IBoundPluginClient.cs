namespace WeavePort.Sdk.Client;
/// <summary>A client whose immutable tenant identity comes from its trusted host binding.</summary>
public interface IBoundPluginClient : IPluginClient
{
    /// <summary>Resolves authenticated binding identity without invoking plugin code. Custom implementations are trusted.</summary>
    Task<string> GetTenantAsync(CancellationToken cancellationToken = default);
}
