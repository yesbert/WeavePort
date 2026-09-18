using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

namespace WeavePort.Composition;
/// <summary>Technical composition primitives. Products own contracts, session identity, ordering and merge semantics.</summary>
public static class Composition
{
    /// <summary>Executes one externally bound plugin over bounded base64 chunks. The plugin must implement the bulk-map contract.</summary>
    public static Task<ResultHandle> MapAsync(ResultScope scope, ResultHandle input, IPluginSession session, int chunkBytes = 65536, CancellationToken cancellationToken = default)
    {
        if (!StringComparer.Ordinal.Equals(scope.Tenant, session.Tenant))
        {
            throw new UnauthorizedAccessException("Plugin binding belongs to another tenant.");
        }

        return scope.TransformAsync(input, async (bytes, token) =>
        {
            InvocationResult result = await session.InvokeAsync("bulk-map", JsonSerializer.SerializeToElement(new BulkChunk(bytes), BulkJson.Default.BulkChunk), token);
            if (result.Status != "ok")
            {
                throw new IOException("Plugin stage failed: " + result.Status);
            }

            token.ThrowIfCancellationRequested();
            return Decode(result.Value, chunkBytes);
        }, chunkBytes, cancellationToken);
    }

    /// <summary>Maps bounded chunks through an SDK operation after verifying local or authenticated remote binding identity. The caller retains client ownership.</summary>
    public static async Task<ResultHandle> MapAsync(ResultScope scope, ResultHandle input, IBoundPluginClient client, int chunkBytes = 65536, CancellationToken cancellationToken = default)
    {
        string tenant = await client.GetTenantAsync(cancellationToken);
        if (!StringComparer.Ordinal.Equals(scope.Tenant, tenant))
        {
            throw new UnauthorizedAccessException("Plugin binding belongs to another tenant.");
        }

        return await scope.TransformAsync(input, async (bytes, token) =>
        {
            JsonElement result = await client.CallAsync("bulk-map", JsonSerializer.SerializeToElement(new BulkChunk(bytes), BulkJson.Default.BulkChunk), token);
            token.ThrowIfCancellationRequested();
            return Decode(result, chunkBytes);
        }, chunkBytes, cancellationToken);
    }

    private static byte[] Decode(JsonElement value, int chunkBytes)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String || !data.TryGetBytesFromBase64(out byte[]? decoded) || decoded.Length > chunkBytes)
        {
            throw new InvalidDataException("Invalid or oversized bulk-map data.");
        }

        return decoded;
    }

    /// <summary>Runs required branches with bounded parallelism. Any failure cancels siblings and awaits started operations.</summary>
    public static async Task<ResultHandle[]> FanOutAsync(ResultScope scope, ResultHandle input, IReadOnlyList<Func<ResultScope, ResultHandle, CancellationToken, Task<ResultHandle>>> branches, int maximumParallelism, CancellationToken cancellationToken = default)
    {
        if (maximumParallelism < 1 || branches.Count is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumParallelism));
        }

        var results = new ResultHandle[branches.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, branches.Count), new ParallelOptions { MaxDegreeOfParallelism = maximumParallelism, CancellationToken = cancellationToken }, async (index, token) => results[index] = await branches[index](scope, input, token));
        return results;
    }

    /// <summary>Concatenates immutable results in declared order. Suitable for NDJSON streams; this is not a generic semantic merge or reranker.</summary>
    public static Task<ResultHandle> ConcatenateAsync(ResultScope scope, IReadOnlyList<ResultHandle> inputs, CancellationToken cancellationToken = default) => scope.CreateAsync(async (destination, token) =>
    {
        foreach (ResultHandle input in inputs)
        {
            await scope.CopyToAsync(input, destination, token);
        }
    }, cancellationToken);
}
