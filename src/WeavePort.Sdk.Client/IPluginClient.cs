using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WeavePort.Sdk.Client;
/// <summary>Product-facing calls independent of local or worker-host deployment.</summary>
public interface IPluginClient : IAsyncDisposable
{
    /// <summary>Invokes an ordinary function and returns its complete bounded JSON value.</summary>
    Task<JsonElement> CallAsync(string operation, JsonElement input, CancellationToken cancellationToken = default);
    /// <summary>Enumerates results incrementally; exceptions after items mean the stream did not complete successfully.</summary>
    IAsyncEnumerable<JsonElement> StreamAsync(string operation, JsonElement input, CancellationToken cancellationToken = default);
}

/// <summary>Typed conveniences using the same contract for every deployment.</summary>
public static class PluginClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Invokes a typed function using web JSON conventions.</summary>
    public static async Task<TOutput> CallAsync<TInput, TOutput>(this IPluginClient client, string operation, TInput input, CancellationToken cancellationToken = default) => (await client.CallAsync(operation, JsonSerializer.SerializeToElement(input, Json), cancellationToken)).Deserialize<TOutput>(Json)!;
    /// <summary>Enumerates typed records without collecting the whole result.</summary>
    public static async IAsyncEnumerable<TItem> StreamAsync<TInput, TItem>(this IPluginClient client, string operation, TInput input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (JsonElement item in client.StreamAsync(operation, JsonSerializer.SerializeToElement(input, Json), cancellationToken))
        {
            yield return item.Deserialize<TItem>(Json)!;
        }
    }
}

/// <summary>Failed SDK operation; failure does not imply that external effects did not occur.</summary>
public sealed class PluginCallException(string status, bool mayHaveExecuted = true) : IOException("Plugin operation: " + status)
{
    /// <summary>Host or SDK failure classification.</summary>
    public string Status { get; } = status;
    /// <summary>Whether dispatch may already have caused effects.</summary>
    public bool MayHaveExecuted { get; } = mayHaveExecuted;
}
