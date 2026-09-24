using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WeavePort.Sdk.Client;
/// <summary>Typed conveniences using the same contract for every deployment.</summary>
public static class PluginClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    /// <summary>Invokes a typed function using web JSON conventions.</summary>
    public static async Task<TOutput> CallAsync<TInput, TOutput>(this IPluginClient client, string operation, TInput input, CancellationToken cancellationToken = default) => (await client.CallAsync(operation, JsonSerializer.SerializeToElement(input, Json), cancellationToken)).Deserialize<TOutput>(Json)!;
    /// <summary>Invokes a typed function and retains timing belonging to this result.</summary>
    public static async Task<PluginCallResult<TOutput>> CallWithMetadataAsync<TInput, TOutput>(this IPluginClient client, string operation, TInput input, CancellationToken cancellationToken = default)
    {
        var result = await client.CallWithMetadataAsync(operation, JsonSerializer.SerializeToElement(input, Json), cancellationToken);
        return new(result.Value.Deserialize<TOutput>(Json)!, result.ElapsedMs);
    }

    /// <summary>Enumerates typed records without collecting the whole result.</summary>
    public static async IAsyncEnumerable<TItem> StreamAsync<TInput, TItem>(this IPluginClient client, string operation, TInput input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (JsonElement item in client.StreamAsync(operation, JsonSerializer.SerializeToElement(input, Json), cancellationToken))
        {
            yield return item.Deserialize<TItem>(Json)!;
        }
    }
}
