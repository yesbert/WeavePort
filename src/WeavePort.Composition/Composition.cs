using WeavePort.Internal;
using System.Text.Json;
using WeavePort.Abstractions;
using WeavePort.Sdk.Client;

namespace WeavePort.Composition;
/// <summary>Technical composition primitives. Products own contracts, session identity, ordering and merge semantics.</summary>
public static class Composition
{
    /// <summary>Collects an exclusive plugin byte source into an atomic, quota-bound result. The caller retains client ownership.</summary>
    public static async Task<ResultHandle> CollectAsync(ResultScope scope, IBoundPluginClient client, string operation, JsonElement input, int chunkBytes = ProtocolLimits.SourceChunkDefaultBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (chunkBytes is < ProtocolLimits.SourceChunkMinimumBytes or > ProtocolLimits.SourceChunkMaximumBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkBytes));
        }

        if (!StringComparer.Ordinal.Equals(scope.Tenant, await client.GetTenantAsync(cancellationToken)))
        {
            throw new UnauthorizedAccessException("Plugin binding belongs to another tenant.");
        }

        return await scope.CreateAsync(async (destination, token) =>
        {
            await foreach (ReadOnlyMemory<byte> block in client.SourceAsync(operation, input, chunkBytes, token).WithCancellation(token))
            {
                if (block.Length > chunkBytes)
                {
                    throw new InvalidDataException("Source chunk exceeds bound.");
                }

                await destination.WriteAsync(block, token);
            }
        }, cancellationToken);
    }

    private const string BulkMapOperation = "bulk-map";
    /// <summary>Executes one externally bound plugin over bounded base64 chunks. The plugin must implement the bulk-map contract.</summary>
    public static Task<ResultHandle> MapAsync(ResultScope scope, ResultHandle input, IPluginSession session, int chunkBytes = ProtocolLimits.SourceChunkDefaultBytes, CancellationToken cancellationToken = default)
    {
        if (!StringComparer.Ordinal.Equals(scope.Tenant, session.Tenant))
        {
            throw new UnauthorizedAccessException("Plugin binding belongs to another tenant.");
        }

        return scope.TransformAsync(input, async (bytes, token) =>
        {
            InvocationResult result = await session.InvokeAsync(BulkMapOperation, JsonSerializer.SerializeToElement(new BulkChunk(bytes), BulkJson.Default.BulkChunk), token);
            if (result.Status != FailureCodes.Ok)
            {
                throw new IOException("Plugin stage failed: " + result.Status);
            }

            token.ThrowIfCancellationRequested();
            return Decode(result.Value, chunkBytes);
        }, chunkBytes, cancellationToken);
    }

    /// <summary>Maps bounded chunks through an SDK operation after verifying local or authenticated remote binding identity. The caller retains client ownership.</summary>
    public static async Task<ResultHandle> MapAsync(ResultScope scope, ResultHandle input, IBoundPluginClient client, int chunkBytes = ProtocolLimits.SourceChunkDefaultBytes, CancellationToken cancellationToken = default)
    {
        string tenant = await client.GetTenantAsync(cancellationToken);
        if (!StringComparer.Ordinal.Equals(scope.Tenant, tenant))
        {
            throw new UnauthorizedAccessException("Plugin binding belongs to another tenant.");
        }

        return await scope.TransformAsync(input, async (bytes, token) =>
        {
            JsonElement result = await client.CallAsync(BulkMapOperation, JsonSerializer.SerializeToElement(new BulkChunk(bytes), BulkJson.Default.BulkChunk), token);
            token.ThrowIfCancellationRequested();
            return Decode(result, chunkBytes);
        }, chunkBytes, cancellationToken);
    }

    private static byte[] Decode(JsonElement value, int chunkBytes)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(WireFields.Data, out var data) || data.ValueKind != JsonValueKind.String || !data.TryGetBytesFromBase64(out byte[]? decoded) || decoded.Length > chunkBytes)
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
